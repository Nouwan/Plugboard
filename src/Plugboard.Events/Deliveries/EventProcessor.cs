using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Runs the deliveries published on the channel and writes their outcomes back.</summary>
internal sealed partial class EventProcessor<TContext>(
    IDbContextFactory<TContext> contexts,
    Channel<EventDelivery> channel,
    IEnumerable<Subscription> subscriptions,
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<EventProcessorOptions> options,
    ILogger<EventProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    private readonly EventProcessorOptions _options = options.Value;
    private readonly HostId _host = new(options.Value.Host);
    private readonly Dictionary<string, Subscription> _subscriptions = Index(subscriptions);

    /// <summary>How long a worker waits before retrying a database write that threw.</summary>
    internal TimeSpan StoreRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, _options.Concurrency).Select(_ => WorkAsync(stoppingToken)));

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        await foreach (EventDelivery found in channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessAsync(found, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogProcessingCrashed(exception, found.EventId, found.Subscription);
            }
        }
    }

    /// <summary>
    /// Runs one delivery and returns whether its outcome was recorded, which it is not when the row was written since
    /// this host reserved it: another host took it over, or this is a stale copy.
    /// </summary>
    internal async Task<bool> ProcessAsync(EventDelivery reserved, CancellationToken cancellationToken)
    {
        Subscription? subscription = _subscriptions.GetValueOrDefault(reserved.Subscription);
        Outcome outcome;

        try
        {
            if (subscription is null)
            {
                outcome = new Outcome.DeadLettered(new InvalidOperationException($"No subscription named '{reserved.Subscription}' is registered on this host."));
            }
            else if (reserved.Attempts > subscription.MaxAttempts)
            {
                outcome = new Outcome.DeadLettered(new InvalidOperationException($"Attempt {reserved.Attempts} is past the limit of {subscription.MaxAttempts}."));
            }
            else
            {
                outcome = await RunAsync(reserved, subscription, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the handler, whose exceptions RunAsync records: something around it failed.
            outcome = subscription is null ? new Outcome.DeadLettered(exception) : Failed(reserved, subscription, exception);
        }

        return await RecordAsync(reserved, outcome, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Subscriptions by name; a name registered by two modules is caught here, at host start, rather than by a dictionary.</summary>
    private static Dictionary<string, Subscription> Index(IEnumerable<Subscription> subscriptions)
    {
        var index = new Dictionary<string, Subscription>(StringComparer.Ordinal);

        foreach (Subscription subscription in subscriptions)
        {
            if (!index.TryAdd(subscription.Name, subscription))
            {
                throw new InvalidOperationException($"Subscription '{subscription.Name}' is registered more than once on this host; names are the delivery key and must be unique across modules.");
            }
        }

        return index;
    }

    private async Task<Outcome> RunAsync(EventDelivery reserved, Subscription subscription, CancellationToken cancellationToken)
    {
        var handlerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        var context = new HandlerContext(reserved.Event.Id, reserved.Event.OccurredAt, reserved.Attempts);
        Task? run = null;
        var abandoned = false;

        try
        {
            run = subscription.Invoke(reserved.Event, context, scope.ServiceProvider, handlerStop.Token);
            await run.WaitAsync(subscription.MaxDuration, time, cancellationToken).ConfigureAwait(false);

            return context.PostponedBy is { } delay
                ? new Outcome.Postponed(time.GetUtcNow() + delay)
                : new Outcome.Processed();
        }
        catch (TimeoutException) when (run is { IsCompleted: false })
        {
            // The handler is still going: it is told to stop and abandoned with its scope, which lives until it ends. The worker moves on.
            abandoned = true;
            _ = AbandonAsync(run, scope, handlerStop, reserved.EventId, reserved.Subscription);
            return Failed(reserved, subscription, new TimeoutException($"Subscription '{reserved.Subscription}' exceeded its limit of {subscription.MaxDuration}."));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(reserved, subscription, exception);
        }
        finally
        {
            if (!abandoned)
            {
                handlerStop.Dispose();
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Waits for a handler that ran past its limit, then disposes what it was running in.</summary>
    private async Task AbandonAsync(Task run, AsyncServiceScope scope, CancellationTokenSource handlerStop, Guid eventId, string subscription)
    {
        DateTimeOffset abandonedAt = time.GetUtcNow();

        try
        {
            await handlerStop.CancelAsync().ConfigureAwait(false);
            await run.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Its outcome is recorded as a timeout already.
        }
        finally
        {
            handlerStop.Dispose();
            await scope.DisposeAsync().ConfigureAwait(false);
            LogAbandonedEnded(eventId, subscription, time.GetUtcNow() - abandonedAt);
        }
    }

    private Outcome Failed(EventDelivery reserved, Subscription subscription, Exception exception) =>
        reserved.Attempts >= subscription.MaxAttempts
            ? new Outcome.DeadLettered(exception)
            : new Outcome.Retried(time.GetUtcNow() + _options.Backoff(reserved.Attempts), exception);

    /// <summary>Writes the outcome over the reserved row and returns whether that row was still there.</summary>
    private async Task<bool> FinishAsync(EventDelivery reserved, Outcome outcome, CancellationToken cancellationToken)
    {
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (outcome is Outcome.Postponed or Outcome.Retried)
        {
            EventDelivery deferred = reserved.Defer(outcome is Outcome.Postponed postponed ? postponed.Until : ((Outcome.Retried)outcome).At);
            int rows = await Reserved(context, reserved)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.NotBefore, deferred.NotBefore)
                    .SetProperty(d => d.UpdatedAt, deferred.UpdatedAt)
                    .SetProperty(d => d.Version, deferred.Version), cancellationToken)
                .ConfigureAwait(false);

            return rows == 1;
        }

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            int removed = await Reserved(context, reserved).ExecuteDeleteAsync(ct).ConfigureAwait(false);

            if (removed != 1)
            {
                return false;
            }

            if (outcome is Outcome.DeadLettered deadLettered)
            {
                context.DeadLetters.Add(reserved.DeadLettered(deadLettered.Error));
            }
            else
            {
                context.DeliveryHistory.Add(reserved.Finished());
            }

            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The row as this host reserved it: same id, still this host's, not written since.</summary>
    private IQueryable<EventDelivery> Reserved(TContext context, EventDelivery reserved) =>
        context.Deliveries.Where(d => d.Id == reserved.Id && d.LockedBy == _host && d.Version == reserved.Version);

    /// <summary>Retries a database write until it succeeds.</summary>
    private async Task<bool> WriteAsync(EventDelivery delivery, Func<CancellationToken, Task<bool>> write, CancellationToken cancellationToken)
    {
        var failed = false;

        while (true)
        {
            try
            {
                return await write(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (!failed)
                {
                    failed = true;
                    LogStoreUnavailable(exception, delivery.EventId, delivery.Subscription, StoreRetryDelay);
                }
            }

            await Task.Delay(StoreRetryDelay, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> RecordAsync(EventDelivery reserved, Outcome outcome, CancellationToken cancellationToken)
    {
        if (!await WriteAsync(reserved, ct => FinishAsync(reserved, outcome, ct), cancellationToken).ConfigureAwait(false))
        {
            LogRowLost(reserved.EventId, reserved.Subscription);
            return false;
        }

        EventEnvelope envelope = reserved.Event;

        switch (outcome)
        {
            case Outcome.Processed:
                LogProcessed(envelope.Id, envelope.Name, reserved.Subscription, reserved.Attempts);
                break;
            case Outcome.Postponed postponed:
                LogPostponed(envelope.Id, envelope.Name, reserved.Subscription, postponed.Until, reserved.Attempts);
                break;
            case Outcome.Retried retried:
                LogRetry(retried.Error, envelope.Id, envelope.Name, reserved.Subscription, reserved.Attempts, retried.At);
                break;
            case Outcome.DeadLettered deadLettered:
                LogDeadLettered(deadLettered.Error, envelope.Id, envelope.Name, reserved.Subscription, reserved.Attempts);
                break;
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The handler of {Subscription} for event {EventId} ignored its cancellation and ended {After} after its limit")]
    private partial void LogAbandonedEnded(Guid eventId, string subscription, TimeSpan after);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing event {EventId} for {Subscription} crashed outside the handler")]
    private partial void LogProcessingCrashed(Exception exception, Guid eventId, string subscription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {EventId} for {Subscription} was written by another host since this host reserved it; its outcome is not recorded here")]
    private partial void LogRowLost(Guid eventId, string subscription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The database is unavailable while writing event {EventId} for {Subscription}; retrying every {Delay} until it answers")]
    private partial void LogStoreUnavailable(Exception exception, Guid eventId, string subscription, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed event {EventId} {EventName} with {Subscription} (attempt {Attempt})")]
    private partial void LogProcessed(Guid eventId, string eventName, string subscription, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Postponed event {EventId} {EventName} for {Subscription} until {Until} (attempt {Attempt})")]
    private partial void LogPostponed(Guid eventId, string eventName, string subscription, DateTimeOffset until, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {EventId} {EventName} failed in {Subscription} (attempt {Attempt}), retry at {RetryAt}")]
    private partial void LogRetry(Exception exception, Guid eventId, string eventName, string subscription, int attempt, DateTimeOffset retryAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {EventId} {EventName} dead-lettered for {Subscription} after {Attempts} attempts")]
    private partial void LogDeadLettered(Exception exception, Guid eventId, string eventName, string subscription, int attempts);
}
