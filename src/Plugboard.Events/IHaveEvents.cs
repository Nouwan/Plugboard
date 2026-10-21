using System.Collections;
using System.Text.Json;

namespace Plugboard.Events;

/// <summary>
/// An entity that records events while it changes. A backend stores the recorded events together with the entity
/// and clears the collection once they are stored.
/// </summary>
public interface IHaveEvents
{
    /// <summary>Events recorded since the last save.</summary>
    EventCollection Events { get; }
}

/// <summary>Events recorded by an <see cref="IHaveEvents"/> entity, serialized on <see cref="Add{TEvent}"/>.</summary>
public sealed class EventCollection : IReadOnlyCollection<EventEnvelope>
{
    private readonly List<EventEnvelope> _events = [];

    /// <inheritdoc/>
    public int Count => _events.Count;

    /// <summary>Records an event; any type marked <see cref="EventAttribute"/>.</summary>
    public void Add<TEvent>(TEvent integrationEvent) where TEvent : IEvent => _events.Add(new EventEnvelope(
        Guid.CreateVersion7(),
        TEvent.EventName,
        JsonSerializer.Serialize(integrationEvent, EventJson.Options),
        DateTimeOffset.UtcNow));

    /// <summary>Snapshot of the recorded events.</summary>
    public EventEnvelope[] GetEvents() => [.. _events];

    /// <summary>Drops the recorded events; called by the backend once they are stored.</summary>
    public void ClearEvents() => _events.Clear();

    /// <inheritdoc/>
    public IEnumerator<EventEnvelope> GetEnumerator() => _events.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
