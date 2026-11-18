using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>
/// The one context that owns the event tables: its migrations create them, and the processor runs on it. Given to
/// <c>AddPlugboardEvents</c>.
/// </summary>
public interface IEventStoreContext
{
    /// <summary>The finished deliveries.</summary>
    DbSet<DeliveryHistory> DeliveryHistory { get; }

    /// <summary>The deliveries that failed for the last time.</summary>
    DbSet<DeadLetter> DeadLetters { get; }

    /// <summary>The hosts running the processor and their last heartbeat.</summary>
    DbSet<EventProcessor> EventProcessors { get; }

    /// <summary>Published events</summary>
    DbSet<EventEnvelope> Events { get; }

    /// <summary>Queued deliveries</summary>
    DbSet<DeliveryQueueItem> DeliveryQueue { get; }
}