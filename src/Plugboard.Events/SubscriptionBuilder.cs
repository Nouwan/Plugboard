using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Plugboard.Events;

/// <summary>Registers subscriptions.</summary>
public static class SubscriptionServiceCollectionExtensions
{
    /// <summary>Runs <paramref name="subscribe"/> against a builder and registers the resulting subscriptions. Called by the generated <c>AddSubscriptions()</c>.</summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services, Action<ISubscriptionBuilder> subscribe)
    {
        var builder = new SubscriptionBuilder();
        subscribe(builder);
        return services.AddSingleton(builder.Build());
    }
}

/// <summary>Collects handlers subscribed through <see cref="ISubscriptionBuilder"/>; the <c>Subscribe</c> overloads only exist to be rewritten by the generator.</summary>
internal sealed class SubscriptionBuilder : ISubscriptionBuilder
{
    private readonly List<Subscription> _handlers = [];

    public ISubscriptionBuilder Subscribe(string name, Delegate handler) => throw NotIntercepted(name);

    public ISubscriptionBuilder Subscribe(string name, TimeSpan maxDuration, Delegate handler) => throw NotIntercepted(name);

    public ISubscriptionBuilder SubscribeAll(string name, Delegate handler) => throw NotIntercepted(name);

    public ISubscriptionBuilder SubscribeAll(string name, TimeSpan maxDuration, Delegate handler) => throw NotIntercepted(name);

    public ISubscriptionBuilder Register<TEvent>(string name, TimeSpan? maxDuration, SubscriptionInvoker<TEvent> invoke) where TEvent : IEvent =>
        Add(name, TEvent.EventName, maxDuration, (envelope, context, scope, ct) =>
            invoke(JsonSerializer.Deserialize<TEvent>(envelope.Payload, EventJson.Options)!, envelope, context, scope, ct));

    public ISubscriptionBuilder RegisterAll(string name, TimeSpan? maxDuration, SubscriptionInvoker<EventEnvelope> invoke) =>
        Add(name, eventName: null, maxDuration, (envelope, context, scope, ct) => invoke(envelope, envelope, context, scope, ct));

    public SubscriptionRegistry Build()
    {
        IEnumerable<string> duplicates = _handlers
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        return duplicates.FirstOrDefault() is { } duplicate
            ? throw new InvalidOperationException($"Event handler name '{duplicate}' is subscribed more than once; names are the delivery key and must be unique.")
            : new SubscriptionRegistry { Handlers = [.. _handlers] };
    }

    private SubscriptionBuilder Add(string name, string? eventName, TimeSpan? maxDuration, EventDeliveryInvoker invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _handlers.Add(new Subscription(name, eventName, maxDuration ?? SubscriptionDefaults.MaxDuration, invoke));
        return this;
    }

    private static InvalidOperationException NotIntercepted(string name) => new(
        $"Event handler '{name}' was subscribed with Subscribe(...) or SubscribeAll(...) but the call was not rewritten at compile time. " +
        "Plugboard.Events generates the typed registration; make sure the package reference is present and the " +
        "Plugboard.Events.Generated namespace is listed in InterceptorsNamespaces (the package's build targets do this).");
}
