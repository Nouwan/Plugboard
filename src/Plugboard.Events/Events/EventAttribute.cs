namespace Plugboard.Events;

/// <summary>Marks a type as an event with a unique name.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EventAttribute(string name) : Attribute
{
    /// <summary>The unique name of the event.</summary>
    public string Name { get; } = name;
}
