using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.SystemStatus;

internal sealed record BuiltInSensorDefinition(string Key, string Name, bool SupportsService, bool SupportsTrayApp);

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
        new("cpu_usage", "CPU terheles", SupportsService: true, SupportsTrayApp: true),
        new("memory_usage", "Memoria hasznalat", SupportsService: true, SupportsTrayApp: true),
        new("memory_available_mb", "Szabad memoria", SupportsService: true, SupportsTrayApp: true),
        new("system_drive_free_percent", "Rendszermeghajto szabad %", SupportsService: true, SupportsTrayApp: true),
        new("system_drive_free_gb", "Rendszermeghajto szabad hely", SupportsService: true, SupportsTrayApp: true),
        new("uptime_seconds", "Uptime", SupportsService: true, SupportsTrayApp: true),
        new("battery_level", "Akkumulator", SupportsService: true, SupportsTrayApp: true),
        new("power_status", "Tapellatas", SupportsService: true, SupportsTrayApp: true),
        new("network_address", "LAN IP", SupportsService: true, SupportsTrayApp: true),
        new("session_state", "Munkamenet allapot", SupportsService: true, SupportsTrayApp: true),
        new("logged_in_user", "Bejelentkezett felhasznalo", SupportsService: true, SupportsTrayApp: true),
        new("pending_reboot", "Ujrainditas szukseges", SupportsService: true, SupportsTrayApp: true),
        new("boot_time", "Boot ido", SupportsService: true, SupportsTrayApp: true),
        new("battery_time_remaining", "Akkumulator hatralevo ido", SupportsService: true, SupportsTrayApp: true),
        new("vpn_connected", "VPN csatlakozva", SupportsService: true, SupportsTrayApp: true),
        new("wifi_ssid", "Wi-Fi SSID", SupportsService: true, SupportsTrayApp: true),
        new("wifi_signal", "Wi-Fi jelerosseg", SupportsService: true, SupportsTrayApp: true),
        new("logged_in_users", "Bejelentkezett felhasznalok", SupportsService: true, SupportsTrayApp: true),
        new("rdp_sessions", "RDP munkamenetek", SupportsService: true, SupportsTrayApp: true),
        new("bluetooth_enabled", "Bluetooth engedelyezve", SupportsService: true, SupportsTrayApp: true),
        new("windows_update_pending", "Windows Update fuggoben", SupportsService: true, SupportsTrayApp: true),
        new("event_log_errors_recent", "Friss esemenynaplo hibak", SupportsService: true, SupportsTrayApp: true),
        new("last_shutdown_reason", "Utolso leallitas oka", SupportsService: true, SupportsTrayApp: true),
        new("active_window", "Aktiv ablak", SupportsService: false, SupportsTrayApp: true),
        new("active_process", "Aktiv processz", SupportsService: false, SupportsTrayApp: true),
        new("foreground_app_title", "Aktiv alkalmazas es ablak", SupportsService: false, SupportsTrayApp: true),
        new("volume", "Hangero", SupportsService: false, SupportsTrayApp: true),
        new("muted", "Nemitva", SupportsService: false, SupportsTrayApp: true),
        new("monitor_power_state", "Monitor allapot", SupportsService: false, SupportsTrayApp: true),
        new("active_display", "Aktiv kijelzok", SupportsService: false, SupportsTrayApp: true),
        new("idle_time_seconds", "Inaktiv ido", SupportsService: false, SupportsTrayApp: true),
        new("session_locked", "Munkamenet zarolva", SupportsService: false, SupportsTrayApp: true),
        new("user_present", "Felhasznalo jelen van", SupportsService: false, SupportsTrayApp: true),
        new("clipboard_text_available", "Vagolapon szoveg", SupportsService: false, SupportsTrayApp: true),
        new("audio_output_device", "Audio kimenet", SupportsService: false, SupportsTrayApp: true),
        new("microphone_muted", "Mikrofon nemitva", SupportsService: false, SupportsTrayApp: true)
    ];

    public static IReadOnlySet<string> AllKeys { get; } = Sensors
        .Select(sensor => sensor.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static BuiltInSensorDefinition? Find(string key)
    {
        return Sensors.FirstOrDefault(sensor => string.Equals(sensor.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}
