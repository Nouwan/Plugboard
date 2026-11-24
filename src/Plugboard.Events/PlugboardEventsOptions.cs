namespace Plugboard.Events;

/// <summary>Configuration of plugboard events</summary>
public sealed class PlugboardEventsOptions
{
    /// <summary>How often this host sweeps the queue for due deliveries nobody owns.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A node without a heartbeat for this long is taken for dead and its deliveries are freed.</summary>
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How many deliveries this host runs at the same time.</summary>
    public int Concurrency { get; set; } = 8;

    /// <summary>How many deliveries the in-process channel holds before saving events waits for room.</summary>
    public int ChannelCapacity { get; set; } = 1_000;
}