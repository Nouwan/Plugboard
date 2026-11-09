namespace Plugboard.Events;

/// <summary>Tuning of the sweeper and the processor.</summary>
public sealed class EventProcessorOptions
{
    /// <summary>The name this host locks deliveries and heartbeats under.</summary>
    public string Host { get; set; } = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    /// <summary>How often this host sweeps the queue for due deliveries nobody owns.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A host without a heartbeat for this long is taken for dead and its deliveries are freed.</summary>
    public TimeSpan HostTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How many deliveries this host runs at the same time.</summary>
    public int Concurrency { get; set; } = 8;

    /// <summary>How many deliveries the in-process channel holds before saving events waits for room.</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>The wait before retry number <c>n</c>.</summary>
    public Func<int, TimeSpan> Backoff { get; set; } = static attempt =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, attempt - 1)));
}