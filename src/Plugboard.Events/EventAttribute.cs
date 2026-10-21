namespace Plugboard.Events;

/// <summary>
/// Marks a <c>partial</c> type as an event and names it. The name is what gets stored with the event and what
/// handlers are matched on, so it must be unique and stable; renaming the type does not change it.
/// </summary>
/// <param name="name">Stable, unique name of the event, for example <c>order.placed.v1</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EventAttribute(string name) : Attribute
{
    /// <summary>Stable, unique name of the event.</summary>
    public string Name { get; } = name;
}
