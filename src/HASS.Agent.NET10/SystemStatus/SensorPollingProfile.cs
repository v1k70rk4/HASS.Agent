namespace HASS.Agent.Companion.SystemStatus;

internal enum SensorPollingProfile
{
    Fast,
    Normal,
    Hourly,
    Startup
}

internal static class SensorPollingProfiles
{
    public static IReadOnlySet<SensorPollingProfile> All { get; } = Enum.GetValues<SensorPollingProfile>().ToHashSet();

    public static string ToKey(SensorPollingProfile profile)
    {
        return profile.ToString().ToLowerInvariant();
    }

    public static SensorPollingProfile FromKey(string? value, SensorPollingProfile fallback)
    {
        if (Enum.TryParse<SensorPollingProfile>(value, ignoreCase: true, out var profile))
        {
            return profile;
        }

        return fallback;
    }

    public static string NormalizeKey(string? value, SensorPollingProfile fallback)
    {
        return ToKey(FromKey(value, fallback));
    }
}
