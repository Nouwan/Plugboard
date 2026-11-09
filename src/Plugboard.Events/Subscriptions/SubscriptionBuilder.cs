using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Plugboard.Events;

/// <summary>Registers subscriptions.</summary>
public static class SubscriptionServiceCollectionExtensions
{
    /// <summary>Registers the subscriptions made by <paramref name="subscribe"/>.</summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services, Action<SubscriptionBuilder> subscribe)
    {
        var builder = new SubscriptionBuilder();
        subscribe(builder);

        foreach (Subscription subscription in builder.Build())
        {
            services.AddSingleton(subscription);
        }

        return services;
    }
}

/// <summary>Subscribes handlers to events; the calls are rewritten into typed registrations at compile time.</summary>
public sealed class SubscriptionBuilder
{
    private readonly List<Subscription> _handlers = [];

    internal SubscriptionBuilder()
    {
    }

    /// <summary>Subscribes <paramref name="handler"/> to the event of its first parameter.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The call is rewritten by the generator and needs the instance.")]
    public Subscription Subscribe(string name, Delegate handler) => throw NotIntercepted(name);

    /// <summary>Subscribes <paramref name="handler"/> to every event.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The call is rewritten by the generator and needs the instance.")]
    public Subscription SubscribeAll(string name, Delegate handler) => throw NotIntercepted(name);

    internal Subscription[] Build()
    {
        IEnumerable<string> duplicates = _handlers
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        return duplicates.FirstOrDefault() is { } duplicate
            ? throw new InvalidOperationException($"Event handler name '{duplicate}' is subscribed more than once; names are the delivery key and must be unique.")
            : [.. _handlers];
    }

    internal Subscription Add(Subscription subscription)
    {
        _handlers.Add(subscription);
        return subscription;
    }

    private static InvalidOperationException NotIntercepted(string name) => new(
        $"Event handler '{name}' was subscribed with Subscribe(...) or SubscribeAll(...) but the call was not rewritten at compile time. " +
        "Plugboard.Events generates the typed registration; make sure the package reference is present and the " +
        "Plugboard.Events.Generated namespace is listed in InterceptorsNamespaces (the package's build targets do this).");
}
