namespace Plugboard.Events;

/// <summary>The names of the event tables.</summary>
public static class EventTables
{
    /// <summary>The table of stored events.</summary>
    public const string Events = "Events";

    /// <summary>The table of deliveries waiting to be processed.</summary>
    public const string DeliveryQueue = "DeliveryQueue";

    /// <summary>The table of deliveries that failed for the last time.</summary>
    public const string DeliveryDeadLetters = "DeliveryDeadLetters";

    /// <summary>The table of finished deliveries.</summary>
    public const string DeliveryHistory = "DeliveryHistory";

    /// <summary>The table of hosts and their last heartbeat.</summary>
    public const string Hosts = "EventHosts";
}
