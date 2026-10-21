using System.ComponentModel;

namespace Plugboard.Events;

/// <summary>
/// Subscribes handlers to events the way minimal APIs map routes. A <see cref="Subscribe(string, Delegate)"/> handler's
/// first parameter is the event, a type marked <see cref="EventAttribute"/>; a <see cref="SubscribeAll(string, Delegate)"/>
/// handler's first parameter is the <see cref="EventEnvelope"/> and it receives every event. Further parameters are
/// resolved from the delivery scope: <see cref="HandlerContext"/>, <see cref="CancellationToken"/>, or any service.
/// The generator rewrites each call at compile time, so nothing is inspected at runtime.
/// </summary>
public interface ISubscriptionBuilder
{
    /// <summary>Subscribes <paramref name="handler"/> to the event of its first parameter under the stable <paramref name="name"/>, with the default <see cref="SubscriptionDefaults.MaxDuration"/>.</summary>
    ISubscriptionBuilder Subscribe(string name, Delegate handler);

    /// <summary>Subscribes <paramref name="handler"/> to the event of its first parameter under the stable <paramref name="name"/>, cancelled after <paramref name="maxDuration"/>.</summary>
    ISubscriptionBuilder Subscribe(string name, TimeSpan maxDuration, Delegate handler);

    /// <summary>Subscribes <paramref name="handler"/> to every event under the stable <paramref name="name"/>, with the default <see cref="SubscriptionDefaults.MaxDuration"/>.</summary>
    ISubscriptionBuilder SubscribeAll(string name, Delegate handler);

    /// <summary>Subscribes <paramref name="handler"/> to every event under the stable <paramref name="name"/>, cancelled after <paramref name="maxDuration"/>.</summary>
    ISubscriptionBuilder SubscribeAll(string name, TimeSpan maxDuration, Delegate handler);

    /// <summary>Target of the generated rewrite of <see cref="Subscribe(string, Delegate)"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ISubscriptionBuilder Register<TEvent>(string name, TimeSpan? maxDuration, SubscriptionInvoker<TEvent> invoke) where TEvent : IEvent;

    /// <summary>Target of the generated rewrite of <see cref="SubscribeAll(string, Delegate)"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ISubscriptionBuilder RegisterAll(string name, TimeSpan? maxDuration, SubscriptionInvoker<EventEnvelope> invoke);
}

/// <summary>Calls a subscribed handler with the deserialized event, or the envelope for handlers receiving every event.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Task SubscriptionInvoker<in TEvent>(TEvent integrationEvent, EventEnvelope envelope, HandlerContext context, IServiceProvider scope, CancellationToken cancellationToken);

/// <summary>Defaults applied to subscriptions.</summary>
public static class SubscriptionDefaults
{
    /// <summary>How long a handler may run before it is cancelled and the delivery is treated as failed.</summary>
    public static TimeSpan MaxDuration => TimeSpan.FromSeconds(30);
}
