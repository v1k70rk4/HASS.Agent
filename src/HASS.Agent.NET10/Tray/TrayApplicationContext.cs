using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Http;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Networking;
using HASS.Agent.Companion.Runtime;
using HASS.Agent.Companion.SystemService;

namespace HASS.Agent.Companion.Tray;

internal sealed class TrayApplicationContext : ApplicationContext, INotificationSink
{
    private readonly CompanionSettings _settings;
    private readonly AppPaths _paths;
    private readonly FileLog _log;
    private readonly Control _uiInvoker = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly List<ActionNotificationForm> _actionNotifications = [];
    private string? _pendingNotificationAction;

    public event EventHandler<NotificationActionRequestedEventArgs>? NotificationActionRequested;
    public event EventHandler? SettingsSaved;

    public TrayApplicationContext(CompanionSettings settings, AppPaths paths, FileLog log)
    {
        _settings = settings;
        _paths = paths;
        _log = log;
        _uiInvoker.CreateControl();
        _ = _uiInvoker.Handle;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = AppIdentity.DisplayName,
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowStatus();
        _notifyIcon.BalloonTipClicked += (_, _) => PublishPendingNotificationAction();
    }

    public void ShowStartupError(Exception exception)
    {
        MessageBox.Show(
            $"The Local API could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
            AppIdentity.DisplayName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowNotification(NotificationPayload notification)
    {
        if (_uiInvoker.IsDisposed)
        {
            return;
        }

        if (_uiInvoker.InvokeRequired)
        {
            try
            {
                _uiInvoker.BeginInvoke(() => ShowNotificationOnUiThread(notification));
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Unable to dispatch notification to UI thread.");
            }
            return;
        }

        ShowNotificationOnUiThread(notification);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var actionNotification in _actionNotifications.ToList())
            {
                actionNotification.Close();
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _uiInvoker.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Status", null, (_, _) => ShowStatus());
        menu.Items.Add("MQTT beállítások", null, (_, _) => ShowMqttSettings());
        menu.Items.Add("Kepessegek / szerepkorok", null, (_, _) => ShowCapabilitiesSettings());
        menu.Items.Add("Szenzorok", null, (_, _) => ShowCustomSensors());
        menu.Items.Add(BuildServiceMenu());
        menu.Items.Add("Copy Local API URL", null, (_, _) => CopyApiUrl());
        menu.Items.Add("Open settings folder", null, (_, _) => OpenSettingsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        return menu;
    }

    private ToolStripMenuItem BuildServiceMenu()
    {
        var serviceMenu = new ToolStripMenuItem("System service");
        serviceMenu.DropDownItems.Add("Status", null, (_, _) => ShowServiceStatus());
        serviceMenu.DropDownItems.Add(new ToolStripSeparator());
        serviceMenu.DropDownItems.Add("Telepítés / frissítés", null, (_, _) => RunServiceCommand("--install-service"));
        serviceMenu.DropDownItems.Add("Indítás", null, (_, _) => RunServiceCommand("--start-service"));
        serviceMenu.DropDownItems.Add("Leállítás", null, (_, _) => RunServiceCommand("--stop-service"));
        serviceMenu.DropDownItems.Add("Eltávolítás", null, (_, _) => RunServiceCommand("--uninstall-service"));

        return serviceMenu;
    }

    private void ShowStatus()
    {
        var lanUrls = string.Join(Environment.NewLine, NetworkInfo.GetLanUrls(_settings.Port));

        MessageBox.Show(
            $"Device: {_settings.DeviceName}{Environment.NewLine}" +
            $"Listening on: {_settings.ListenUrl}{Environment.NewLine}" +
            $"Use in Home Assistant:{Environment.NewLine}{lanUrls}{Environment.NewLine}" +
            $"Settings: {_paths.SettingsFile}{Environment.NewLine}" +
            $"Log: {_paths.LogFile}",
            AppIdentity.DisplayName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void CopyApiUrl()
    {
        Clipboard.SetText(NetworkInfo.GetPreferredLanUrl(_settings.Port));
        _notifyIcon.ShowBalloonTip(3000, AppIdentity.DisplayName, "Home Assistant URL copied.", ToolTipIcon.Info);
    }

    private void ShowMqttSettings()
    {
        using var form = new MqttSettingsForm(_settings, _paths);
        form.SettingsSaved += (_, _) => SettingsSaved?.Invoke(this, EventArgs.Empty);
        form.ShowDialog();
    }

    private void ShowCapabilitiesSettings()
    {
        using var form = new CapabilitiesSettingsForm(_settings, _paths);
        form.SettingsSaved += (_, _) => SettingsSaved?.Invoke(this, EventArgs.Empty);
        form.ShowDialog();
    }

    private void ShowCustomSensors()
    {
        using var form = new CustomSensorsForm(_settings, _paths);
        form.SettingsSaved += (_, _) => SettingsSaved?.Invoke(this, EventArgs.Empty);
        form.ShowDialog();
    }

    private static void ShowServiceStatus()
    {
        MessageBox.Show(
            CompanionServiceManager.GetStatusText(),
            AppIdentity.ServiceDisplayName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RunServiceCommand(string command)
    {
        CompanionServiceManager.RunElevated(command, _log);
        _notifyIcon.ShowBalloonTip(3000, AppIdentity.DisplayName, "System service parancs elindítva.", ToolTipIcon.Info);
    }

    private void OpenSettingsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.ConfigDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unable to open settings folder.");
            MessageBox.Show(ex.Message, AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowNotificationOnUiThread(NotificationPayload notification)
    {
        if (notification.HasActions)
        {
            _log.Info($"Showing actionable notification with {notification.Actions.Count} action(s).");
            ShowActionNotification(notification);
            return;
        }

        var title = string.IsNullOrWhiteSpace(notification.Title)
            ? "Home Assistant"
            : notification.Title.Trim();

        _pendingNotificationAction = notification.PrimaryAction;

        _notifyIcon.ShowBalloonTip(
            notification.TimeoutMilliseconds,
            title,
            notification.Message!.Trim(),
            ToolTipIcon.Info);
    }

    private void ShowActionNotification(NotificationPayload notification)
    {
        var form = new ActionNotificationForm(notification, action =>
        {
            NotificationActionRequested?.Invoke(this, new NotificationActionRequestedEventArgs(action));
        });

        _actionNotifications.Add(form);
        form.FormClosed += (_, _) =>
        {
            _actionNotifications.Remove(form);
            RepositionActionNotifications();
        };

        RepositionActionNotifications();
        form.Show();
    }

    private void RepositionActionNotifications()
    {
        var bottomOffset = 16;
        foreach (var form in _actionNotifications.AsEnumerable().Reverse())
        {
            form.PositionFromBottom(bottomOffset);
            bottomOffset += form.Height + 12;
        }
    }

    private void PublishPendingNotificationAction()
    {
        var action = _pendingNotificationAction;
        _pendingNotificationAction = null;

        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        NotificationActionRequested?.Invoke(this, new NotificationActionRequestedEventArgs(action.Trim()));
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "hassagent.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }
}

internal sealed class NotificationActionRequestedEventArgs(string action) : EventArgs
{
    public string Action { get; } = action;
}
