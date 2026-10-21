using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugboard.Events;

/// <summary>
/// Serializer settings for stored payloads. Owned here, not by the host, so rows written months ago stay readable
/// whatever the application's own JSON configuration is.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EventJson
{
    /// <summary>Case-insensitive, enums as strings so rows survive renumbering.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
