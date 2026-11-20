using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Writes this host's heartbeat and frees the deliveries of every owner without a live one.</summary>
internal sealed partial class InstanceHeartBeatService<TContext>(
    IDbContextFactory<TContext> contexts,
    IOptions<PlugboardEventsOptions> options,
    TimeProvider time,
    ILogger<InstanceHeartBeatService<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    private readonly PlugboardEventsOptions _options = options.Value;
    private readonly InstanceId _instance = InstanceId.Current;

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await BeatAsync(cancellationToken).ConfigureAwait(false);
        await FreeDeadHostsAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases this host's rows after the other event services stopped.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, _instance, _options.HostTimeout);
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.HostTimeout / 6, time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await BeatAsync(stoppingToken).ConfigureAwait(false);
                await FreeDeadHostsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogHeartbeatFailed(exception, _instance, _options.HostTimeout);
            }
        }
    }

    /// <summary>Writes this host's heartbeat.</summary>
    private async Task BeatAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        int seen = await context.EventProcessors
            .Where(h => h.Id == _instance)
            .ExecuteUpdateAsync(setters => setters.SetProperty(h => h.LastSeen, now), cancellationToken)
            .ConfigureAwait(false);

        if (seen == 0)
        {
            context.EventProcessors.Add(new ProcessorInstance { Id = _instance, LastSeen = now });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Frees the rows of every owner without a live heartbeat, dead hosts and hosts that never wrote one, and forgets the dead hosts.</summary>
    private async Task FreeDeadHostsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset seenSince = time.GetUtcNow() - _options.HostTimeout;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        int freed = await context.DeliveryQueue
            .Where(d => d.LockedBy != null && !context.EventProcessors.Any(h => h.Id == d.LockedBy && h.LastSeen >= seenSince))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, (InstanceId?)null)
                .SetProperty(d => d.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        await context.EventProcessors
            .Where(h => h.LastSeen < seenSince)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (freed > 0)
        {
            LogFreed(freed);
        }
    }

    /// <summary>Releases every row this host owns and removes the host.</summary>
    private async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await context.DeliveryQueue
            .Where(d => d.LockedBy == _instance)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, (InstanceId?)null)
                .SetProperty(d => d.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        await context.EventProcessors
            .Where(h => h.Id == _instance)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Could not write the heartbeat of host {Host}; if this keeps failing for {HostTimeout} the other hosts free its deliveries")]
    private partial void LogHeartbeatFailed(Exception exception, InstanceId instance, TimeSpan hostTimeout);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Releasing the deliveries of host {Host} on shutdown failed; they free up after {HostTimeout}")]
    private partial void LogReleaseFailed(Exception exception, InstanceId instance, TimeSpan hostTimeout);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Freed {Freed} deliveries of hosts without a live heartbeat")]
    private partial void LogFreed(int freed);
}
