using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.Media;

internal sealed class MediaCommand
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }
}
