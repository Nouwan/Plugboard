namespace Plugboard.Events;

/// <summary>Delivery information for a handler.</summary>
public sealed record HandlerContext(Guid EventId, DateTimeOffset OccurredAt, int Attempt)
{
    /// <summary>The delay the handler asked for or null.</summary>
    public TimeSpan? PostponedBy { get; private set; }

    /// <summary>Asks to be offered the delivery again after <paramref name="delay"/>.</summary>
    public void Postpone(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        PostponedBy = delay;
    }
}