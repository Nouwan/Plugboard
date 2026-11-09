namespace Plugboard.Events;

/// <summary>What a worker decided about a delivery after running, or refusing to run, its handler.</summary>
internal abstract record Outcome
{
    /// <summary>The handler completed: the delivery moves to the history.</summary>
    public sealed record Processed : Outcome;

    /// <summary>The handler asked to be offered the delivery again at <paramref name="Until"/>.</summary>
    public sealed record Postponed(DateTimeOffset Until) : Outcome;

    /// <summary>The attempt failed: the delivery is retried at <paramref name="At"/>.</summary>
    public sealed record Retried(DateTimeOffset At, Exception Error) : Outcome;

    /// <summary>The delivery failed for the last time: it moves to the dead letters.</summary>
    public sealed record DeadLettered(Exception Error) : Outcome;
}
