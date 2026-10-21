using System.ComponentModel;

namespace Plugboard.Events;

/// <summary>
/// Implemented by the generator on every <c>partial</c> type marked <see cref="EventAttribute"/>. Never implement it
/// by hand; it only exists so the name is known from the type without reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEvent
{
    /// <summary>Name given by <see cref="EventAttribute"/>; what is stored and what handlers are matched on.</summary>
    static abstract string EventName { get; }
}
