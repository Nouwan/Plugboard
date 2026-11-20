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
internal sealed partial class DeliveryQueueSweeper<TContext>(
    IDbContextFactory<TContext> contexts,
    Channel<DeliveryQueueItem> channel,
    IOptions<PlugboardEventsOptions> options,
    TimeProvider time,
    ILogger<DeliveryQueueSweeper<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    internal const int BatchSize = 5;

    private readonly PlugboardEventsOptions _options = options.Value;
    private readonly InstanceId _instance = InstanceId.Current;

    private static TimeSpan Jittered(TimeSpan interval)
    {
        long shortest = (long)Math.Floor(interval.TotalMilliseconds * 0.8);
        long longest = (long)Math.Ceiling(interval.TotalMilliseconds * 1.2);

        var delay = Random.Shared.NextInt64(shortest, longest);
        return TimeSpan.FromMilliseconds(delay);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        int postponed = await RepeatUntilEmptyAsync(SweepPostponedAsync, ct).ConfigureAwait(false);
        int unclaimed = await RepeatUntilEmptyAsync(SweepUnclaimedAsync, ct).ConfigureAwait(false);
        int created = await RepeatUntilEmptyAsync(SweepNewAsync, ct).ConfigureAwait(false);

        LogSwept(postponed + unclaimed + created);
    }

    private static async Task<int> RepeatUntilEmptyAsync(Func<CancellationToken, Task<int>> sweepPage,
        CancellationToken ct)
    {
        int total = 0;
        int count;

        do
        {
            count = await sweepPage(ct).ConfigureAwait(false);
            total += count;
        } while (count > 0);

        return total;
    }

    private async Task<int> SweepPostponedAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<DeliveryQueueItem> page = await context.DeliveryQueue
            .Include(d => d.Event)
            .Where(d => (d.LockedBy == _instance) && (d.NotBefore <= now))
            .OrderBy(d => d.NotBefore)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (DeliveryQueueItem delivery in page)
        {
            await PublishAsync(context, delivery, ct).ConfigureAwait(false);
        }

        return page.Count;
    }

    private async Task<int> SweepUnclaimedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<DeliveryQueueItem> page = await context.DeliveryQueue
            .Include(d => d.Event)
            .Where(d => (d.LockedBy == null) && (d.NotBefore <= now))
            .OrderBy(d => d.NotBefore)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (DeliveryQueueItem delivery in page)
        {
            await PublishAsync(context, delivery, cancellationToken).ConfigureAwait(false);
        }

        return page.Count;
    }

    private async Task<int> SweepNewAsync(CancellationToken cancellationToken)
    {
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<DeliveryQueueItem> page = await context.DeliveryQueue
            .Include(d => d.Event)
            .Where(d => (d.LockedBy == null) && (d.NotBefore == null))
            .OrderBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (DeliveryQueueItem delivery in page)
        {
            await PublishAsync(context, delivery, cancellationToken).ConfigureAwait(false);
        }

        return page.Count;
    }

    private async Task PublishAsync(TContext context, DeliveryQueueItem delivery, CancellationToken cancellationToken)
    {
        await context.ReserveAsync(delivery, _instance, cancellationToken).ConfigureAwait(false);
        context.Entry(delivery).State = EntityState.Detached;
        await channel.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Jittered(_options.SweepInterval), time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                LogAlreadyClaimed();
            }

            timer.Period = Jittered(_options.SweepInterval);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Swept {Published} due deliveries into the channel")]
    private partial void LogSwept(int published);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A delivery was already claimed by another processor, the rest of this sweep is skipped")]
    private partial void LogAlreadyClaimed();
}