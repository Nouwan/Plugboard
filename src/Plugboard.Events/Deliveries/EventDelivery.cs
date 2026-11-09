using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>
/// The delivery of one stored event to one subscription, waiting in the queue. Free when <see cref="LockedBy"/> is
/// null, waiting until <see cref="NotBefore"/> when that is set, otherwise handed to a worker of the host that owns it.
/// </summary>
[EntityTypeConfiguration(typeof(EventDeliveryConfiguration))]
public sealed record EventDelivery
{
    /// <summary>The id of the delivery.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The stored event this delivery belongs to.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The subscription name.</summary>
    public required string Subscription { get; init; }

    /// <summary>The event being delivered.</summary>
    public EventEnvelope Event { get; init; } = null!;

    /// <summary>How many times the delivery was handed to a worker.</summary>
    public int Attempts { get; init; }

    /// <summary>The host that owns the delivery or null when it is free.</summary>
    public HostId? LockedBy { get; init; }

    /// <summary>The delivery is not offered to a worker before this time.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>When the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Counts the writes to the row; a write is only applied to the version it was based on.</summary>
    public int Version { get; init; }

    /// <summary>A host takes the delivery for a worker: the attempt counts from here, whatever becomes of it.</summary>
    internal EventDelivery Reserve(HostId host) => this with
    {
        LockedBy = host,
        NotBefore = null,
        Attempts = Attempts + 1,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = Version + 1,
    };

    /// <summary>Keeps the delivery on the same host until <paramref name="until"/>, for a retry or a postponement.</summary>
    internal EventDelivery Defer(DateTimeOffset until) => this with
    {
        NotBefore = until,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = Version + 1,
    };

    /// <summary>Frees the delivery for any host to take.</summary>
    internal EventDelivery Free() => this with
    {
        LockedBy = null,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = Version + 1,
    };

    /// <summary>The history row of this delivery.</summary>
    internal DeliveryHistory Finished() => new()
    {
        Id = Id,
        EventId = EventId,
        Subscription = Subscription,
        Attempts = Attempts,
        FinishedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>The dead letter of this delivery.</summary>
    internal DeadLetter DeadLettered(Exception exception) => new()
    {
        Id = Id,
        EventId = EventId,
        Subscription = Subscription,
        Attempts = Attempts,
        Error = $"{exception.GetType().FullName}: {exception.Message}",
        FailedAt = DateTimeOffset.UtcNow,
    };
}
