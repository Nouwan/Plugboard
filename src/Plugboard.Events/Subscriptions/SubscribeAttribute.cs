namespace Plugboard.Events;

/// <summary>Marks a method that subscribes handlers to events.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SubscribeAttribute : Attribute;
