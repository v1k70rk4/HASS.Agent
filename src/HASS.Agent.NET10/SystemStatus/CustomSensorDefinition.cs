using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.SystemStatus;

internal sealed class CustomSensorDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Type { get; set; } = "process_running";

    public string Name { get; set; } = "Custom sensor";

    public string Parameter { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool Service { get; set; } = true;

    public bool TrayApp { get; set; } = true;

    [JsonIgnore]
    public bool IsProcessRunning => string.Equals(Type, CustomSensorTypes.ProcessRunning, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsServiceStatus => string.Equals(Type, CustomSensorTypes.ServiceStatus, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsDiskFree => string.Equals(Type, CustomSensorTypes.DiskFree, StringComparison.OrdinalIgnoreCase);
}

internal static class CustomSensorTypes
{
    public const string ProcessRunning = "process_running";
    public const string ServiceStatus = "service_status";
    public const string DiskFree = "disk_free";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ProcessRunning,
        ServiceStatus,
        DiskFree
    };

    public static string Normalize(string value)
    {
        return All.Contains(value) ? value.Trim().ToLowerInvariant() : ProcessRunning;
    }
}

internal sealed record CustomSensorDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("device_class")] string? DeviceClass,
    [property: JsonPropertyName("state_class")] string? StateClass,
    [property: JsonPropertyName("icon")] string Icon);

internal sealed record CustomSensorState(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] object? Value);
