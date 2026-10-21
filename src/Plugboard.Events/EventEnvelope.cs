namespace Plugboard.Events;

/// <summary>
/// A stored event: its name, its serialized payload and when it occurred. Handlers taking an
/// <see cref="EventEnvelope"/> receive every event regardless of type.
/// </summary>
/// <param name="Id">Identifier of the stored event.</param>
/// <param name="Name">Name given by <see cref="EventAttribute"/>.</param>
/// <param name="Payload">The event serialized as JSON.</param>
/// <param name="OccurredAt">When the event was raised.</param>
public sealed record EventEnvelope(Guid Id, string Name, string Payload, DateTimeOffset OccurredAt);
