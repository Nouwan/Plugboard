namespace Plugboard.Events;

/// <summary>Delivery information for a handler.</summary>
public sealed record SubscriptionContext(Guid EventId, DateTimeOffset OccurredAt, int Attempt);