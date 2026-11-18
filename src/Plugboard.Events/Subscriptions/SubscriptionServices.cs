using System.ComponentModel;
using System.Reflection;

namespace Plugboard.Events;

/// <summary>Typed registration targets for generated code only.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubscriptionServices
{
    /// <summary>Registers <paramref name="invoke"/> for <typeparamref name="TEvent"/> under <paramref name="name"/>.</summary>
    public static Subscription Register<TEvent>(SubscriptionBuilder events, string name, Func<TEvent, SubscriptionContext, IServiceProvider, CancellationToken, Task> invoke) =>
        events.Add(new Subscription(name, typeof(TEvent).GetCustomAttribute<EventAttribute>()!.Name,
            (envelope, context, scope, ct) => invoke(envelope.Read<TEvent>(), context, scope, ct)));
}