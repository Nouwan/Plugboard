using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Runs the deliveries published on the channel and writes their outcomes back.</summary>
internal sealed partial class DeliveryQueueProcessor<TContext>(
    IDbContextFactory<TContext> contextFactory,
    Channel<DeliveryQueueItem> channel,
    ILogger<DeliveryQueueProcessor<TContext>> logger,
    IEnumerable<Subscription> subscriptions,
    IServiceScopeFactory scopes,
    IOptions<PlugboardEventsOptions> options,
    TimeProvider time) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    private readonly PlugboardEventsOptions _options = options.Value;

    private readonly Dictionary<string, Subscription> _subscriptions =
        subscriptions.ToDictionary(s => s.Name, StringComparer.Ordinal);

    private Task[] CreateAndStartWorkers(int workerCount, CancellationToken ct)
        => Enumerable.Range(0, workerCount)
            .Select(_ => ReadEventsFromChannelAsync(ct))
            .ToArray();

    private async Task ReadEventsFromChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (DeliveryQueueItem delivery in channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await DeliverAsync(delivery, stoppingToken).ConfigureAwait(false);
            LogDelivered(delivery.Id);
        }
    }

    private async Task DeliverAsync(DeliveryQueueItem delivery, CancellationToken stoppingToken)
    {
        await using TContext context = await contextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
        context.DeliveryQueue.Attach(delivery);

        if (!_subscriptions.TryGetValue(delivery.Subscription, out Subscription? subscription))
        {
            await context.DeadLetterAsync(delivery,
                    $"No subscription named '{delivery.Subscription}' is registered on this host.", time.GetUtcNow(), stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        if (delivery.Attempts > subscription.MaxAttempts)
        {
            await context.DeadLetterAsync(delivery,
                    $"Attempt {delivery.Attempts} is past the limit of {subscription.MaxAttempts}.", time.GetUtcNow(), stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeLimit.CancelAfter(subscription.MaxDuration);

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            var subscriptionContext =
                new SubscriptionContext(delivery.Event.Id, delivery.Event.OccurredAt, delivery.Attempts);

            await subscription.Invoke(delivery.Event, subscriptionContext, scope.ServiceProvider, timeLimit.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            if (delivery.Attempts >= subscription.MaxAttempts)
            {
                await context.DeadLetterAsync(delivery, exception, time.GetUtcNow(), stoppingToken);
                return;
            }

            await context.RetryAsync(delivery, time.GetUtcNow(), stoppingToken);
            return;
        }

        await context.FinishAsync(delivery, time.GetUtcNow(), stoppingToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task[] workers = CreateAndStartWorkers(_options.Concurrency, stoppingToken);
        Task first = await Task.WhenAny(workers).ConfigureAwait(false);

        if (first.IsFaulted)
        {
            //Rethrows the exception that failed the task
            await first.ConfigureAwait(false);
        }

        //If no exception has been thrown wait for the other workers to end before shutdown
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Information, "Event delivery with id {EventDeliveryId} ran")]
    partial void LogDelivered(Guid eventDeliveryId);
}