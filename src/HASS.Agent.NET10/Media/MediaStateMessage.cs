using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.Media;

internal sealed class MediaStateMessage
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("albumtitle")]
    public string? AlbumTitle { get; init; }

    [JsonPropertyName("albumartist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("duration")]
    public double Duration { get; init; }

    [JsonPropertyName("currentposition")]
    public double CurrentPosition { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = "off";

    [JsonPropertyName("volume")]
    public int Volume { get; init; }

    [JsonPropertyName("muted")]
    public bool Muted { get; init; }
}
