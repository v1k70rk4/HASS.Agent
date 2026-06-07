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
using HASS.Agent.Companion.Localization;
using HASS.Agent.Companion.Tray;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows.Forms;

namespace HASS.Agent.Companion;

internal static class Program
{
    private const string MutexName = "Local\\HASS.Agent.NET10";
    private const string ExitEventName = "Local\\HASS.Agent.NET10.Exit";

    [STAThread]
    private static void Main()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Any(arg => string.Equals(arg, "--exit", StringComparison.OrdinalIgnoreCase)))
        {
            SignalExistingInstanceToExit();
            return;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            if (args.Any(arg => string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            InitializeWindowsForms();
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

        InitializeWindowsForms();

        // Load language early so elevated service commands show localized messages
        LoadLanguageEarly();

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

        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var exitRegistration = ThreadPool.RegisterWaitForSingleObject(
            exitEvent,
            (_, _) => Application.Exit(),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);

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

        Localization.Strings.Language = settings.Language;
        Localization.Strings.HaLanguage = settings.HaLanguage;
        try
        {
            if (!settings.AutoStartOnLogin && StartupManager.IsEnabled())
            {
                settings.AutoStartOnLogin = true;
                SettingsStore.Save(paths, settings);
                log.Info("Adopted auto-start setting from registry (installer).");
            }

            StartupManager.SetEnabled(settings.AutoStartOnLogin);
        }
        catch (Exception ex)
        {
            log.Warning($"Unable to update startup registration: {ex.Message}");
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
        using var localApi = new LocalApiServer(settings, trayContext, mediaSessionService, log);

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

        if (!settings.MqttEnabled)
        {
            trayContext.ShowMqttSetupRequired();
        }

        if (settings.ShowStartupNotification)
        {
            trayContext.ShowNotification(new NotificationPayload
            {
                Title = AppIdentity.DisplayName,
                Message = $"Home Assistant URL: {NetworkInfo.GetPreferredLanUrl(settings.Port)}"
            });
        }

        Application.Run(trayContext);
        exitRegistration.Unregister(null);

        mqttService.StopAsync().GetAwaiter().GetResult();
        localApi.StopAsync().GetAwaiter().GetResult();
        log.Info($"Stopped {AppIdentity.DisplayName}.");
    }

    private static void SignalExistingInstanceToExit()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEvent))
            {
                using (exitEvent)
                {
                    exitEvent.Set();
                }
            }
        }
        catch
        {
            // Best-effort helper for installers and scripts.
        }
    }

    private static void InitializeWindowsForms()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
    }

    private static void LoadLanguageEarly()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppIdentity.ConfigurationDirectoryName,
                "settings.json");

            if (!File.Exists(settingsPath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));

            if (doc.RootElement.TryGetProperty("language", out var lang))
            {
                Localization.Strings.Language = lang.GetString() ?? "hu";
            }

            if (doc.RootElement.TryGetProperty("haLanguage", out var haLang))
            {
                Localization.Strings.HaLanguage = haLang.GetString() ?? "hu";
            }
        }
        catch
        {
            // Settings file may not exist yet or be unreadable — keep defaults.
        }
    }
}
