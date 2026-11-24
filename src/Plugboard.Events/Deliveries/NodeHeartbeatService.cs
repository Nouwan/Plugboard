using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Writes this host's heartbeat and frees its queued deliveries on shutdown.</summary>
internal sealed partial class NodeHeartbeatService<TContext>(
    IDbContextFactory<TContext> contexts,
    IOptions<PlugboardEventsOptions> options,
    TimeProvider time,
    ILogger<NodeHeartbeatService<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    private const int BeatsPerTimeout = 6;

    private readonly PlugboardEventsOptions _options = options.Value;
    private readonly NodeId _node = NodeId.Current;

    private async Task BeatAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        int seen = await context.DeliveryNodes
            .Where(node => node.Id == _node)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.LastSeen, now), ct)
            .ConfigureAwait(false);

        if (seen == 0)
        {
            context.DeliveryNodes.Add(new DeliveryNode { Id = _node, LastSeen = now });
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReleaseDeliveriesAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        await context.DeliveryQueue
            .Where(delivery => delivery.LockedBy == _node)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(delivery => delivery.LockedBy, (NodeId?)null)
                .SetProperty(delivery => delivery.UpdatedAt, now), ct)
            .ConfigureAwait(false);
    }

    private async Task RemoveNodeAsync(CancellationToken ct)
    {
        await using TContext context = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        await context.DeliveryNodes
            .Where(node => node.Id == _node)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.NodeTimeout / BeatsPerTimeout, time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await BeatAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await BeatAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ReleaseDeliveriesAsync(cancellationToken).ConfigureAwait(false);
            await RemoveNodeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, _node, _options.NodeTimeout);
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Releasing the deliveries of node {Node} on shutdown failed; they free up after {NodeTimeout}")]
    private partial void LogReleaseFailed(Exception exception, NodeId node, TimeSpan nodeTimeout);
}