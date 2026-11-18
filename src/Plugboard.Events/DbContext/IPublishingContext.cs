using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>
/// A context that publishes events.
/// </summary>
public interface IPublishingContext
{
    /// <summary>Published events</summary>
    DbSet<EventEnvelope> Events { get; }

    /// <summary>Event deliveries that </summary>
    DbSet<DeliveryQueueItem> Deliveries { get; }
}