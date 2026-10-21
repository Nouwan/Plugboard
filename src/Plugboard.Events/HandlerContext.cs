namespace Plugboard.Events;

/// <summary>
/// Delivery information a handler cannot derive from the event itself. Deliveries are at-least-once; use
/// <see cref="EventId"/> as the idempotency key and <see cref="Attempt"/> to see whether this is a retry.
/// </summary>
/// <param name="EventId">Identifier of the stored event.</param>
/// <param name="OccurredAt">When the event was raised.</param>
/// <param name="Attempt">1 for the first delivery, incremented on every retry.</param>
public sealed record HandlerContext(Guid EventId, DateTimeOffset OccurredAt, int Attempt);
