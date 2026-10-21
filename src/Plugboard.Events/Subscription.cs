using System.ComponentModel;

namespace Plugboard.Events;

/// <summary>One subscription as the processor sees it.</summary>
/// <param name="Name">Stable name given at subscription time; half of the delivery row key.</param>
/// <param name="EventName">The event handled, or null for a handler receiving every event.</param>
/// <param name="MaxDuration">How long the handler may run before it is cancelled.</param>
/// <param name="Invoke">Deserializes the envelope's payload and calls the handler.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record Subscription(
    string Name,
    string? EventName,
    TimeSpan MaxDuration,
    EventDeliveryInvoker Invoke);

/// <summary>Runs a subscribed handler for one stored event.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Task EventDeliveryInvoker(EventEnvelope envelope, HandlerContext context, IServiceProvider scope, CancellationToken cancellationToken);

/// <summary>The subscriptions made by one assembly's <c>AddSubscriptions()</c>. The processor resolves every instance.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SubscriptionRegistry
{
    /// <summary>All mapped handlers.</summary>
    public required IReadOnlyList<Subscription> Handlers { get; init; }

    /// <summary>The deliveries to store for <paramref name="envelope"/>: one per handler of its event plus one per handler receiving every event.</summary>
    public EventDelivery[] DeliveriesFor(EventEnvelope envelope) => Handlers
        .Where(h => h.EventName is null || h.EventName == envelope.Name)
        .Select(h => new EventDelivery { EventId = envelope.Id, Handler = h.Name })
        .ToArray();
}
