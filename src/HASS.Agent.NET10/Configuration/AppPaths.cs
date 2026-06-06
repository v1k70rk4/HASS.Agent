using HASS.Agent.Companion.Runtime;

namespace HASS.Agent.Companion.Configuration;

internal sealed record AppPaths(string ConfigDirectory, string SettingsFile, string LogFile)
{
    public static AppPaths Create()
    {
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppIdentity.ConfigurationDirectoryName);
        var legacyProgramDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppIdentity.LegacyConfigurationDirectoryName);
        var legacyConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppIdentity.LegacyConfigurationDirectoryName);

        Directory.CreateDirectory(configDirectory);

        var settingsFile = Path.Combine(configDirectory, "settings.json");
        var legacyProgramDataSettingsFile = Path.Combine(legacyProgramDataDirectory, "settings.json");
        var legacySettingsFile = Path.Combine(legacyConfigDirectory, "settings.json");
        if (!File.Exists(settingsFile) && File.Exists(legacyProgramDataSettingsFile))
        {
            File.Copy(legacyProgramDataSettingsFile, settingsFile);
        }
        else if (!File.Exists(settingsFile) && File.Exists(legacySettingsFile))
        {
            File.Copy(legacySettingsFile, settingsFile);
        }

        return new AppPaths(
            configDirectory,
            settingsFile,
            Path.Combine(configDirectory, "hass-agent-net10.log"));
    }
}
