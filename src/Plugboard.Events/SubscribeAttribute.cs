namespace Plugboard.Events;

/// <summary>
/// Marks a method that subscribes handlers to events. The method must be <c>static void</c>, <c>public</c> or <c>internal</c>,
/// and take a single <see cref="ISubscriptionBuilder"/> parameter. Every such method in the assembly is called by the
/// generated <c>services.AddSubscriptions()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SubscribeAttribute : Attribute;
