namespace Plugboard.Events;

/// <summary>The events an entity published, stored by the save of the context that tracks the entity.</summary>
public sealed class EventOutbox
{
    private readonly List<EventEnvelope> _events = [];

    /// <summary>The events published and not stored yet.</summary>
    public IReadOnlyList<EventEnvelope> Published => _events;

    /// <summary>Publishes <paramref name="integrationEvent"/> with the next save of the context that tracks the entity.</summary>
    public void Publish<TEvent>(TEvent integrationEvent) => _events.Add(EventEnvelope.Create(integrationEvent));

    /// <summary>Returns everything in the outbox and empties it</summary>
    internal EventEnvelope[] Drain()
    {
        EventEnvelope[] taken = [.. _events];
        _events.Clear();
        return taken;
    }
}