namespace Plugboard.Events;

/// <summary>
/// One handler's delivery of one stored event; the row a backend stores next to the <see cref="EventEnvelope"/>.
/// Born <see cref="EventDeliveryStatus.Pending"/> when the event is saved, one per handler declared for the event.
/// </summary>
public sealed class EventDelivery
{
    /// <summary>The stored event this delivery belongs to.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The handler, <c>Namespace.Class.Method</c>, as the generated dispatcher knows it.</summary>
    public required string Handler { get; init; }

    /// <summary>Where this delivery is in its life.</summary>
    public EventDeliveryStatus Status { get; set; }

    /// <summary>Number of times a handler run was started.</summary>
    public int Attempts { get; set; }

    /// <summary>Token of the processor that currently owns the delivery, or null.</summary>
    public string? LockedBy { get; set; }

    /// <summary>Until when the owner's lease, or a retry backoff, holds.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>When the handler completed.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Last failure, kept for diagnostics.</summary>
    public string? Error { get; set; }
}

/// <summary>Life of an <see cref="EventDelivery"/>.</summary>
public enum EventDeliveryStatus
{
    /// <summary>Stored, not yet picked up.</summary>
    Pending = 0,

    /// <summary>Owned by a processor.</summary>
    Processing = 1,

    /// <summary>Handler completed.</summary>
    Processed = 2,

    /// <summary>Gave up after the last attempt.</summary>
    Failed = 3,
}
