using System.Security.Cryptography;
using System.Text;

namespace HASS.Agent.Companion.Security;

internal static class ProtectedSecretStore
{
    private const string MachinePrefix = "machine:";
    private const string UserPrefix = "user:";

    public static string Protect(string secret, string entropy)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            BuildEntropy(entropy),
            DataProtectionScope.LocalMachine);

        return $"{MachinePrefix}{Convert.ToBase64String(protectedBytes)}";
    }

    public static string Unprotect(string protectedSecret, string entropy)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return string.Empty;
        }

        try
        {
            var scope = DataProtectionScope.CurrentUser;
            var payload = protectedSecret;

            if (protectedSecret.StartsWith(MachinePrefix, StringComparison.Ordinal))
            {
                scope = DataProtectionScope.LocalMachine;
                payload = protectedSecret[MachinePrefix.Length..];
            }
            else if (protectedSecret.StartsWith(UserPrefix, StringComparison.Ordinal))
            {
                payload = protectedSecret[UserPrefix.Length..];
            }

            var protectedBytes = Convert.FromBase64String(payload);
            var bytes = TryUnprotect(protectedBytes, BuildEntropy(entropy), scope) ??
                TryUnprotect(protectedBytes, BuildLegacyEntropy(entropy), scope);

            return bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsMachineProtected(string protectedSecret)
    {
        return protectedSecret.StartsWith(MachinePrefix, StringComparison.Ordinal);
    }

    private static byte[] BuildEntropy(string entropy)
    {
        return Encoding.UTF8.GetBytes($"HASS.Agent.NET10:{entropy}");
    }

    private static byte[] BuildLegacyEntropy(string entropy)
    {
        return Encoding.UTF8.GetBytes($"HASS.Agent.Companion:{entropy}");
    }

    private static byte[]? TryUnprotect(byte[] protectedBytes, byte[] entropy, DataProtectionScope scope)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes, entropy, scope);
        }
        catch
        {
            return null;
        }
    }
}
