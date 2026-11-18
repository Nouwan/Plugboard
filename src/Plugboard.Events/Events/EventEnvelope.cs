using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Plugboard.Events;

/// <summary>Contains the event and related metadata.</summary>
[EntityTypeConfiguration(typeof(EventEnvelopeConfiguration))]
public sealed class EventEnvelope
{
    private EventEnvelope(Guid id, string name, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Name = name;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Payload { get; }
    public DateTimeOffset OccurredAt { get; }

    internal static EventEnvelope Create<TEvent>(TEvent integrationEvent)
    {
        string name = typeof(TEvent).GetCustomAttribute<EventAttribute>()?.Name
            ?? throw new ArgumentException($"{typeof(TEvent)} is not marked with [Event].", nameof(integrationEvent));
        return new EventEnvelope(Guid.CreateVersion7(), name, JsonSerializer.Serialize(integrationEvent, EventJson.Options), DateTimeOffset.UtcNow);
    }

    internal TEvent Read<TEvent>() => JsonSerializer.Deserialize<TEvent>(Payload, EventJson.Options)!;
}

internal sealed class EventEnvelopeConfiguration : IEntityTypeConfiguration<EventEnvelope>
{
    public void Configure(EntityTypeBuilder<EventEnvelope> builder)
    {
        builder.ToTable(EventTables.Events);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Payload);
        builder.HasIndex(x => x.OccurredAt);
    }
}