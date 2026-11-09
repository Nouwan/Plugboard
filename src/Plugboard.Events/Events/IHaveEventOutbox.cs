namespace Plugboard.Events;

/// <summary>Type that has an event outbox with events to be published on save changes</summary>
public interface IHaveEventOutbox
{
    /// <summary>The events raised since the last save.</summary>
    EventOutbox Events { get; }
}