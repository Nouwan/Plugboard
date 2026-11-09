using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>
/// A context that publishes events. Declaring the sets puts the event tables in the context's model, and only such a
/// context can <see cref="EventContextExtensions.Publish{TContext,TEvent}"/>. Register it with <c>AddEventContext</c>
/// so the events it saves are delivered.
/// </summary>
public interface IEventContext
{
    /// <summary>The stored events.</summary>
    DbSet<EventEnvelope> Events { get; }

    /// <summary>The deliveries waiting to be processed.</summary>
    DbSet<EventDelivery> Deliveries { get; }
}

/// <summary>
/// The one context that owns the event tables: its migrations create them, and the processor runs on it. Given to
/// <c>AddPlugboardEvents</c>.
/// </summary>
public interface IEventStoreContext : IEventContext
{
    /// <summary>The finished deliveries.</summary>
    DbSet<DeliveryHistory> DeliveryHistory { get; }

    /// <summary>The deliveries that failed for the last time.</summary>
    DbSet<DeadLetter> DeadLetters { get; }

    /// <summary>The hosts running the processor and their last heartbeat.</summary>
    DbSet<EventHost> EventHosts { get; }
}
