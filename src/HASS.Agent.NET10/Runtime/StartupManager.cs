using Microsoft.Win32;
using System.Windows.Forms;

namespace HASS.Agent.Companion.Runtime;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(AppIdentity.ExecutableName) is string value &&
            string.Equals(value, BuildRunValue(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            runKey.SetValue(AppIdentity.ExecutableName, BuildRunValue(), RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(AppIdentity.ExecutableName, throwOnMissingValue: false);
        }
    }

    private static string BuildRunValue()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = Application.ExecutablePath;
        }

        return $"\"{executablePath}\"";
    }
}
