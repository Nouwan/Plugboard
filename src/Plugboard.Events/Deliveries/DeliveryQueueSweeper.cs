using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Plugboard.Events.Jitter;

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
    private readonly NodeId _node = NodeId.Current;

    private async Task SweepAsync(CancellationToken ct)
    {
        await RemoveDeadNodesAsync(ct).ConfigureAwait(false);
        await FreeDeliveriesWithoutNodeAsync(ct).ConfigureAwait(false);

        int postponed = await RepeatUntilEmptyAsync(SweepPostponedAsync, ct).ConfigureAwait(false);
        int unclaimed = await RepeatUntilEmptyAsync(SweepUnclaimedAsync, ct).ConfigureAwait(false);
        int created = await RepeatUntilEmptyAsync(SweepNewAsync, ct).ConfigureAwait(false);

        LogSwept(postponed + unclaimed + created);
    }

    private async Task RemoveDeadNodesAsync(CancellationToken ct)
    {
        DateTimeOffset lastSeenBefore = time.GetUtcNow() - _options.NodeTimeout;
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        await context.DeliveryNodes
            .Where(node => node.LastSeen < lastSeenBefore)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task FreeDeliveriesWithoutNodeAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        NodeId[] owners = await context.DeliveryQueue
            .Where(delivery => delivery.LockedBy != null)
            .Select(delivery => delivery.LockedBy!.Value)
            .Distinct()
            .ToArrayAsync(ct)
            .ConfigureAwait(false);

        NodeId[] nodes = await context.DeliveryNodes
            .Select(node => node.Id)
            .ToArrayAsync(ct)
            .ConfigureAwait(false);

        NodeId[] ownersWithoutNode = [.. owners.Except(nodes)];

        if (ownersWithoutNode.Length == 0)
        {
            return;
        }

        int freed = await context.DeliveryQueue
            .Where(delivery => (delivery.LockedBy != null) && ownersWithoutNode.Contains(delivery.LockedBy.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(delivery => delivery.LockedBy, (NodeId?)null)
                .SetProperty(delivery => delivery.UpdatedAt, now), ct)
            .ConfigureAwait(false);

        LogFreed(freed);
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
            .Where(d => (d.LockedBy == _node) && (d.NotBefore <= now))
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
        await context.ReserveAsync(delivery, _node, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Freed {Freed} deliveries of nodes without a live heartbeat")]
    private partial void LogFreed(int freed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Swept {Published} due deliveries into the channel")]
    private partial void LogSwept(int published);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A delivery was already claimed by another processor, the rest of this sweep is skipped")]
    private partial void LogAlreadyClaimed();
}