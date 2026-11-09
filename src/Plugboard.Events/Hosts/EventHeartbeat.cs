using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plugboard.Events;

/// <summary>Writes this host's heartbeat and frees the deliveries of every owner without a live one.</summary>
internal sealed partial class EventHeartbeat<TContext>(
    IDbContextFactory<TContext> contexts,
    IOptions<EventProcessorOptions> options,
    TimeProvider time,
    ILogger<EventHeartbeat<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventStoreContext
{
    private readonly EventProcessorOptions _options = options.Value;
    private readonly HostId _host = new(options.Value.Host);

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
            LogReleaseFailed(exception, _host, _options.HostTimeout);
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
                LogHeartbeatFailed(exception, _host, _options.HostTimeout);
            }
        }
    }

    /// <summary>Writes this host's heartbeat.</summary>
    private async Task BeatAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        int seen = await context.EventHosts
            .Where(h => h.Id == _host)
            .ExecuteUpdateAsync(setters => setters.SetProperty(h => h.LastSeen, now), cancellationToken)
            .ConfigureAwait(false);

        if (seen == 0)
        {
            context.EventHosts.Add(new EventHost(_host, now));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Frees the rows of every owner without a live heartbeat, dead hosts and hosts that never wrote one, and forgets the dead hosts.</summary>
    private async Task FreeDeadHostsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset seenSince = time.GetUtcNow() - _options.HostTimeout;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using TContext context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        int freed = await context.Deliveries
            .Where(d => d.LockedBy != null && !context.EventHosts.Any(h => h.Id == d.LockedBy && h.LastSeen >= seenSince))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, (HostId?)null)
                .SetProperty(d => d.UpdatedAt, now)
                .SetProperty(d => d.Version, d => d.Version + 1), cancellationToken)
            .ConfigureAwait(false);

        await context.EventHosts
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

        await context.Deliveries
            .Where(d => d.LockedBy == _host)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, (HostId?)null)
                .SetProperty(d => d.UpdatedAt, now)
                .SetProperty(d => d.Version, d => d.Version + 1), cancellationToken)
            .ConfigureAwait(false);

        await context.EventHosts
            .Where(h => h.Id == _host)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Could not write the heartbeat of host {Host}; if this keeps failing for {HostTimeout} the other hosts free its deliveries")]
    private partial void LogHeartbeatFailed(Exception exception, HostId host, TimeSpan hostTimeout);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Releasing the deliveries of host {Host} on shutdown failed; they free up after {HostTimeout}")]
    private partial void LogReleaseFailed(Exception exception, HostId host, TimeSpan hostTimeout);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Freed {Freed} deliveries of hosts without a live heartbeat")]
    private partial void LogFreed(int freed);
}
