using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Http;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Media;
using HASS.Agent.Companion.Mqtt;
using HASS.Agent.Companion.Networking;
using HASS.Agent.Companion.Runtime;
using HASS.Agent.Companion.SystemCommands;
using HASS.Agent.Companion.SystemService;
using HASS.Agent.Companion.SystemStatus;
using HASS.Agent.Companion.Tray;
using System.ServiceProcess;
using System.Windows.Forms;

namespace HASS.Agent.Companion;

internal static class Program
{
    private const string MutexName = "Local\\HASS.Agent.NET10";

    [STAThread]
    private static void Main()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            if (args.Any(arg => string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            ApplicationConfiguration.Initialize();
            MessageBox.Show(
                $"{AppIdentity.DisplayName} requires Windows 10 version 2004 (build 19041) or newer. Windows 11 is recommended.",
                AppIdentity.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)))
        {
            ServiceBase.Run(new CompanionWindowsService());
            return;
        }

        ApplicationConfiguration.Initialize();

        if (CompanionServiceManager.TryHandleCommandLine(args))
        {
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                $"{AppIdentity.DisplayName} is already running.",
                AppIdentity.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var paths = AppPaths.Create();
        using var log = new FileLog(paths.LogFile);
        log.Info($"Starting {AppIdentity.DisplayName}.");

        CompanionSettings settings;
        try
        {
            settings = SettingsStore.LoadOrCreate(paths, log);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to load settings.");
            MessageBox.Show(
                $"Unable to load settings: {ex.Message}",
                AppIdentity.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using var trayContext = new TrayApplicationContext(settings, paths, log);
        using var monitorPowerStateService = new MonitorPowerStateService();
        using var mediaSessionService = new MediaSessionService(log);
        using var systemMetricsService = new SystemMetricsService(log, monitorPowerStateService);
        using var systemCommandService = new SystemCommandService(log);
        using var mqttService = new MqttCompanionService(
            settings,
            trayContext,
            mediaSessionService,
            systemMetricsService,
            systemCommandService,
            CompanionRuntimeRole.App,
            log);
        using var localApi = new LocalApiServer(settings, trayContext, log);

        trayContext.NotificationActionRequested += (_, args) =>
        {
            log.Info($"Notification action selected: {args.Action}");
            _ = Task.Run(() => mqttService.PublishNotificationActionAsync(args.Action));
        };
        trayContext.SettingsSaved += (_, _) =>
        {
            log.Info("Settings saved from UI.");
            _ = Task.Run(() => mqttService.RestartAsync());
        };

        try
        {
            localApi.StartAsync().GetAwaiter().GetResult();
            log.Info($"Local API listening on {settings.ListenUrl}.");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to start Local API.");
            trayContext.ShowStartupError(ex);
        }

        mqttService.Start();

        if (settings.ShowStartupNotification)
        {
            trayContext.ShowNotification(new NotificationPayload
            {
                Title = AppIdentity.DisplayName,
                Message = $"Home Assistant URL: {NetworkInfo.GetPreferredLanUrl(settings.Port)}"
            });
        }

        Application.Run(trayContext);

        mqttService.StopAsync().GetAwaiter().GetResult();
        localApi.StopAsync().GetAwaiter().GetResult();
        log.Info($"Stopped {AppIdentity.DisplayName}.");
    }
}
