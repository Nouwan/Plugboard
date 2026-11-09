using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Plugboard.Events;

/// <summary>Contains the event and related metadata.</summary>
[EntityTypeConfiguration(typeof(EventEnvelopeConfiguration))]
public sealed record EventEnvelope
{
    private EventEnvelope(Guid id, string name, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Name = name;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    /// <summary>The id of the event.</summary>
    public Guid Id { get; }

    /// <summary>The name of the event as given by <see cref="EventAttribute"/>.</summary>
    public string Name { get; }

    /// <summary>The event as JSON.</summary>
    public string Payload { get; }

    /// <summary>When the event was created.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Wraps an <c>[Event]</c> with a new id and the current time.</summary>
    public static EventEnvelope Create<TEvent>(TEvent integrationEvent)
    {
        string name = typeof(TEvent).GetCustomAttribute<EventAttribute>()?.Name
            ?? throw new ArgumentException($"{typeof(TEvent)} is not marked with [Event].", nameof(integrationEvent));
        return new EventEnvelope(Guid.CreateVersion7(), name, JsonSerializer.Serialize(integrationEvent, EventJson.Options), DateTimeOffset.UtcNow);
    }

    /// <summary>Reads the event <see cref="Payload"/> as <typeparamref name="TEvent"/>.</summary>
    public TEvent Read<TEvent>() => JsonSerializer.Deserialize<TEvent>(Payload, EventJson.Options)!;
}