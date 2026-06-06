using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.SystemStatus;

internal sealed record BuiltInSensorDefinition(string Key, bool SupportsService, bool SupportsTrayApp);

internal sealed class BuiltInSensorSetting
{
    public string Key { get; set; } = string.Empty;

    public bool Service { get; set; }

    public bool TrayApp { get; set; }
}

internal sealed record BuiltInSensorDescriptor(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name);

internal static class BuiltInSensorCatalog
{
    public static IReadOnlyList<BuiltInSensorDefinition> Sensors { get; } =
    [
        new("cpu_usage", SupportsService: true, SupportsTrayApp: true),
        new("memory_usage", SupportsService: true, SupportsTrayApp: true),
        new("memory_available_mb", SupportsService: true, SupportsTrayApp: true),
        new("system_drive_free_percent", SupportsService: true, SupportsTrayApp: true),
        new("system_drive_free_gb", SupportsService: true, SupportsTrayApp: true),
        new("uptime_seconds", SupportsService: true, SupportsTrayApp: true),
        new("battery_level", SupportsService: true, SupportsTrayApp: true),
        new("power_status", SupportsService: true, SupportsTrayApp: true),
        new("network_address", SupportsService: true, SupportsTrayApp: true),
        new("session_state", SupportsService: true, SupportsTrayApp: true),
        new("logged_in_user", SupportsService: true, SupportsTrayApp: true),
        new("pending_reboot", SupportsService: true, SupportsTrayApp: true),
        new("boot_time", SupportsService: true, SupportsTrayApp: true),
        new("battery_time_remaining", SupportsService: true, SupportsTrayApp: true),
        new("vpn_connected", SupportsService: true, SupportsTrayApp: true),
        new("wifi_ssid", SupportsService: true, SupportsTrayApp: true),
        new("wifi_signal", SupportsService: true, SupportsTrayApp: true),
        new("logged_in_users", SupportsService: true, SupportsTrayApp: true),
        new("rdp_sessions", SupportsService: true, SupportsTrayApp: true),
        new("bluetooth_enabled", SupportsService: true, SupportsTrayApp: true),
        new("windows_update_pending", SupportsService: true, SupportsTrayApp: true),
        new("event_log_errors_recent", SupportsService: true, SupportsTrayApp: true),
        new("last_shutdown_reason", SupportsService: true, SupportsTrayApp: true),
        new("active_window", SupportsService: false, SupportsTrayApp: true),
        new("active_process", SupportsService: false, SupportsTrayApp: true),
        new("foreground_app_title", SupportsService: false, SupportsTrayApp: true),
        new("volume", SupportsService: false, SupportsTrayApp: true),
        new("muted", SupportsService: false, SupportsTrayApp: true),
        new("monitor_power_state", SupportsService: false, SupportsTrayApp: true),
        new("active_display", SupportsService: false, SupportsTrayApp: true),
        new("idle_time_seconds", SupportsService: false, SupportsTrayApp: true),
        new("session_locked", SupportsService: false, SupportsTrayApp: true),
        new("user_present", SupportsService: false, SupportsTrayApp: true),
        new("clipboard_text_available", SupportsService: false, SupportsTrayApp: true),
        new("audio_output_device", SupportsService: false, SupportsTrayApp: true),
        new("microphone_muted", SupportsService: false, SupportsTrayApp: true)
    ];

    public static IReadOnlySet<string> AllKeys { get; } = Sensors
        .Select(sensor => sensor.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static BuiltInSensorDefinition? Find(string key)
    {
        return Sensors.FirstOrDefault(sensor => string.Equals(sensor.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}
