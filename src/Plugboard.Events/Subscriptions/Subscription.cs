namespace Plugboard.Events;

/// <summary>One subscribed handler and its settings.</summary>
public sealed class Subscription
{
    internal Subscription(string name, string? eventName, Func<EventEnvelope, HandlerContext, IServiceProvider, CancellationToken, Task> invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        EventName = eventName;
        Invoke = invoke;
    }

    /// <summary>The stable name of the subscription.</summary>
    public string Name { get; }

    /// <summary>The name of the event handled or null for every event.</summary>
    public string? EventName { get; }

    /// <summary>How long the handler may run before it is cancelled and the delivery counts as failed.</summary>
    public TimeSpan MaxDuration { get; private set; } = SubscriptionDefaults.MaxDuration;

    /// <summary>Attempts after which a delivery is dead-lettered instead of retried.</summary>
    public int MaxAttempts { get; private set; } = SubscriptionDefaults.MaxAttempts;

    /// <summary>Deserializes the envelope's payload and calls the handler.</summary>
    internal Func<EventEnvelope, HandlerContext, IServiceProvider, CancellationToken, Task> Invoke { get; }

    /// <summary>Cancels the handler after <paramref name="maxDuration"/>.</summary>
    public Subscription WithMaxDuration(TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDuration, TimeSpan.Zero);
        MaxDuration = maxDuration;
        return this;
    }

    /// <summary>Dead-letters a delivery after <paramref name="maxAttempts"/> attempts.</summary>
    public Subscription WithMaxAttempts(int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        MaxAttempts = maxAttempts;
        return this;
    }
}

/// <summary>Defaults applied to subscriptions.</summary>
internal static class SubscriptionDefaults
{
    /// <summary>How long a handler may run before it is cancelled and the delivery is treated as failed.</summary>
    public static TimeSpan MaxDuration => TimeSpan.FromSeconds(30);

    /// <summary>Attempts after which a delivery is dead-lettered instead of retried.</summary>
    public static int MaxAttempts => 5;
}
