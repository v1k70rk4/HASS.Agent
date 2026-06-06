using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Runtime;

namespace HASS.Agent.Companion.Tray;

internal sealed class MqttSettingsForm : Form
{
    private readonly CompanionSettings _settings;
    private readonly AppPaths _paths;

    private readonly CheckBox _enabled = new();
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _tls = new();
    private readonly CheckBox _retainDiscovery = new();
    private readonly CheckBox _notifications = new();
    private readonly CheckBox _mediaPlayer = new();
    private readonly CheckBox _buttons = new();
    private readonly CheckBox _systemSensors = new();
    private readonly NumericUpDown _systemSensorsInterval = new();

    public event EventHandler? SettingsSaved;

    public MqttSettingsForm(CompanionSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;

        Text = $"{AppIdentity.DisplayName} - MQTT";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 500);
        Font = new Font("Segoe UI", 9F);

        BuildForm();
        LoadValues();
    }

    private void BuildForm()
    {
        var y = 18;
        AddControl(_enabled, "MQTT engedelyezese", 18, y, 380);
        y += 40;

        AddLabel("Broker host", 18, y);
        AddControl(_host, null, 150, y - 4, 270);
        y += 38;

        AddLabel("Port", 18, y);
        _port.Minimum = 1;
        _port.Maximum = 65535;
        AddControl(_port, null, 150, y - 4, 120);
        y += 38;

        AddLabel("Felhasznalo", 18, y);
        AddControl(_username, null, 150, y - 4, 270);
        y += 38;

        AddLabel("Jelszo", 18, y);
        _password.UseSystemPasswordChar = true;
        AddControl(_password, null, 150, y - 4, 270);
        y += 42;

        AddControl(_tls, "TLS hasznalata", 18, y, 380);
        y += 32;
        AddControl(_retainDiscovery, "Discovery retain flag", 18, y, 380);
        y += 32;
        AddControl(_notifications, "Ertesitesek MQTT-n", 18, y, 380);
        y += 32;
        AddControl(_mediaPlayer, "Media player MQTT-n", 18, y, 380);
        y += 32;
        AddControl(_buttons, "Gombok MQTT-n (szerepkorok alapjan)", 18, y, 380);
        _buttons.Enabled = false;
        y += 32;
        AddControl(_systemSensors, "Gepallapot szenzorok MQTT-n", 18, y, 380);
        y += 38;

        AddLabel("Szenzor frissites", 18, y);
        _systemSensorsInterval.Minimum = 5;
        _systemSensorsInterval.Maximum = 3600;
        AddControl(_systemSensorsInterval, null, 150, y - 4, 120);
        Controls.Add(new Label
        {
            Text = "masodperc",
            Location = new Point(280, y),
            Size = new Size(110, 24)
        });

        var save = new Button
        {
            Text = "Mentés",
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 228, ClientSize.Height - 52)
        };
        save.Click += (_, _) => Save();

        var cancel = new Button
        {
            Text = "Mégse",
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 118, ClientSize.Height - 52)
        };
        cancel.Click += (_, _) => Close();

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(save);
        Controls.Add(cancel);
    }

    private void LoadValues()
    {
        _enabled.Checked = _settings.MqttEnabled;
        _host.Text = _settings.MqttHost;
        _port.Value = _settings.MqttPort;
        _username.Text = _settings.MqttUsername;
        _password.Text = _settings.GetMqttPassword();
        _tls.Checked = _settings.MqttUseTls;
        _retainDiscovery.Checked = _settings.MqttRetainDiscovery;
        _notifications.Checked = _settings.MqttNotificationsEnabled;
        _mediaPlayer.Checked = _settings.MqttMediaPlayerEnabled;
        _buttons.Checked = _settings.TrayAppCommands.Count > 0;
        _systemSensors.Checked = _settings.MqttSystemSensorsEnabled;
        _systemSensorsInterval.Value = _settings.SystemSensorsIntervalSeconds;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_host.Text))
        {
            MessageBox.Show("A broker host nem lehet ures.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.MqttEnabled = _enabled.Checked;
        _settings.MqttHost = _host.Text.Trim();
        _settings.MqttPort = (int)_port.Value;
        _settings.MqttUsername = _username.Text.Trim();
        _settings.SetMqttPassword(_password.Text);
        _settings.MqttUseTls = _tls.Checked;
        _settings.MqttRetainDiscovery = _retainDiscovery.Checked;
        _settings.MqttNotificationsEnabled = _notifications.Checked;
        _settings.MqttMediaPlayerEnabled = _mediaPlayer.Checked;
        _settings.MqttButtonsEnabled = _settings.TrayAppCommands.Count > 0;
        _settings.MqttSystemSensorsEnabled = _systemSensors.Checked;
        _settings.SystemSensorsIntervalSeconds = (int)_systemSensorsInterval.Value;

        SettingsStore.Save(_paths, _settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(120, 24)
        });
    }

    private void AddControl(Control control, string? text, int x, int y, int width)
    {
        control.Location = new Point(x, y);
        control.Size = new Size(width, 26);

        if (control is CheckBox checkbox && text is not null)
        {
            checkbox.Text = text;
            checkbox.AutoSize = false;
        }

        Controls.Add(control);
    }
}
