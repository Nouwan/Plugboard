using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>
/// Periodically publishes the due deliveries on the channel: this host's own retries and postponements, and the
/// deliveries nobody owns, which it reserves first.
/// </summary>
internal sealed partial class EventSweeper<TContext>(
    IDbContextFactory<TContext> contexts,
    Channel<DeliveryQueueItem> channel,
    IOptions<PlugboardEventsOptions> options,
    TimeProvider time,
    ILogger<EventSweeper<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    internal const int Limit = 50;

    private readonly PlugboardEventsOptions _options = options.Value;
    private readonly InstanceId _instance = InstanceId.Current;

    /// <summary>Sweeps at start and then every interval; the intervals drift apart so hosts do not all read the same page at once.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Jittered(_options.SweepInterval), time);

        do
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogSweepFailed(exception);
            }

            timer.Period = Jittered(_options.SweepInterval);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>The interval, give or take a fifth.</summary>
    private static TimeSpan Jittered(TimeSpan interval) => interval * (0.8 + (0.4 * Random.Shared.NextDouble()));

    /// <summary>Sweeps the queue page by page.</summary>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        int published = 0;
        List<DeliveryQueueItem> claimed;

        do
        {
            claimed = await ClaimDueAsync(now, cancellationToken).ConfigureAwait(false);

            foreach (DeliveryQueueItem delivery in claimed)
            {
                await channel.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
            }

            published += claimed.Count;
        }
        while (claimed.Count > 0);

        LogSwept(published);
    }

    /// <summary>Reserves a page of due deliveries for this host.</summary>
    private async Task<List<DeliveryQueueItem>> ClaimDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Own rows with a NotBefore time are retries and postponements; own rows without one are with a worker already.
        List<DeliveryQueueItem> due = await context.DeliveryQueue
            .AsNoTracking()
            .Include(d => d.Event)
            .Where(d => (d.LockedBy == null && (d.NotBefore == null || d.NotBefore <= now)) || (d.LockedBy == _instance && d.NotBefore <= now))
            .OrderBy(d => d.NotBefore == null ? 0 : 1) // Fresh rows first on every provider; SQL Server and PostgreSQL sort nulls differently.
            .ThenBy(d => d.NotBefore)
            .ThenBy(d => d.EventId)
            .Take(Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DeliveryQueueItem> claimed = new(due.Count);

        foreach (DeliveryQueueItem seen in due)
        {
            // Only while the row is still as the page read it: another host may have taken it or a worker may have
            // rescheduled it since. Clearing NotBefore keeps the next page from reading it again.
            if (await context.ReserveAsync(seen, _instance, cancellationToken).ConfigureAwait(false))
            {
                claimed.Add(seen);
            }
        }

        return claimed;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Swept {Published} due deliveries into the channel")]
    private partial void LogSwept(int published);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sweeping the delivery queue failed")]
    private partial void LogSweepFailed(Exception exception);
}
