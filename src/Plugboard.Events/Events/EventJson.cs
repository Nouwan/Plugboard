using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugboard.Events;

/// <summary>The serializer settings for stored payloads.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class EventJson
{
    /// <summary>The options used to serialize payloads.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
