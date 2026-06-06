namespace HASS.Agent.Companion.SystemCommands;

internal static class SystemCommandCatalog
{
    public static IReadOnlyList<SystemCommandDefinition> Commands { get; } =
    [
        new("lock", "Gep zarolasa", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("sleep", "Alvas", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("monitor_off", "Monitor kikapcsolasa", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("volume_up", "Hangero fel", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("volume_down", "Hangero le", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("toggle_mute", "Nemitas valtasa", SupportsTrayApp: true, SupportsService: false, DefaultTrayApp: true, DefaultService: false),
        new("shutdown", "Leallitas", SupportsTrayApp: true, SupportsService: true, DefaultTrayApp: true, DefaultService: true),
        new("restart", "Ujrainditas", SupportsTrayApp: true, SupportsService: true, DefaultTrayApp: true, DefaultService: true),
        new("restart_cancel", "Leallitas/ujrainditas megszakitasa", SupportsTrayApp: true, SupportsService: true, DefaultTrayApp: true, DefaultService: true)
    ];

    public static IReadOnlyList<string> AllCommandNames { get; } = Commands.Select(command => command.Name).ToArray();

    public static IReadOnlyList<string> DefaultTrayAppCommands { get; } = Commands
        .Where(command => command.DefaultTrayApp)
        .Select(command => command.Name)
        .ToArray();

    public static IReadOnlyList<string> DefaultServiceCommands { get; } = Commands
        .Where(command => command.DefaultService)
        .Select(command => command.Name)
        .ToArray();

    public static IReadOnlySet<string> ServiceCapableCommands { get; } = Commands
        .Where(command => command.SupportsService)
        .Select(command => command.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalizeName(string? command, out string normalized)
    {
        normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
        return AllCommandNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record SystemCommandDefinition(
    string Name,
    string DisplayName,
    bool SupportsTrayApp,
    bool SupportsService,
    bool DefaultTrayApp,
    bool DefaultService);
