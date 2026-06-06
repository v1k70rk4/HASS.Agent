namespace HASS.Agent.Companion.Runtime;

internal enum CompanionRuntimeRole
{
    App,
    Service
}

internal static class CompanionRuntimeRoleExtensions
{
    public static string Token(this CompanionRuntimeRole role)
    {
        return role switch
        {
            CompanionRuntimeRole.Service => "service",
            _ => "app"
        };
    }
}
