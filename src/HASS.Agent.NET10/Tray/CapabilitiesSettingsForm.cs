using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Runtime;
using HASS.Agent.Companion.SystemCommands;

namespace HASS.Agent.Companion.Tray;

internal sealed class CapabilitiesSettingsForm : Form
{
    private readonly CompanionSettings _settings;
    private readonly AppPaths _paths;
    private readonly CheckBox _notificationsApp = new();
    private readonly CheckBox _mediaPlayerApp = new();
    private readonly CheckBox _systemSensorsService = new();
    private readonly CheckBox _systemSensorsApp = new();
    private readonly List<CommandRow> _commandRows = [];

    public event EventHandler? SettingsSaved;

    public CapabilitiesSettingsForm(CompanionSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;

        Text = $"{AppIdentity.DisplayName} - kepessegek";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(600, 620);
        Font = new Font("Segoe UI", 9F);

        BuildForm();
        LoadValues();
    }

    private void BuildForm()
    {
        Controls.Add(new Label
        {
            Text = "Mi kezelje es mit hirdessen a Home Assistant fele?",
            Location = new Point(18, 14),
            Size = new Size(560, 24),
            Font = new Font(Font, FontStyle.Bold)
        });

        var scroller = new Panel
        {
            Location = new Point(18, 48),
            Size = new Size(560, 462),
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle
        };

        var table = new TableLayoutPanel
        {
            Location = new Point(0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1 + 3 + 1 + SystemCommandCatalog.Commands.Count,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 342F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        AddHeader(table, "Mi", 0);
        AddHeader(table, "Service", 1);
        AddHeader(table, "Tray app", 2);

        var row = 1;
        AddRowStyle(table);
        AddFeatureRow(table, row++, "Ertesitesek", serviceCheckBox: null, _notificationsApp);
        AddRowStyle(table);
        AddFeatureRow(table, row++, "Media player", serviceCheckBox: null, _mediaPlayerApp);
        AddRowStyle(table);
        AddFeatureRow(table, row++, "Gepallapot szenzorok", _systemSensorsService, _systemSensorsApp);

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        AddSectionRow(table, row++, "Rendszerparancsok");

        foreach (var command in SystemCommandCatalog.Commands)
        {
            AddRowStyle(table);
            var service = command.SupportsService ? new CheckBox() : null;
            var app = new CheckBox { Enabled = command.SupportsTrayApp };
            AddFeatureRow(table, row++, command.DisplayName, service, app);
            _commandRows.Add(new CommandRow(command, service, app));
        }

        scroller.Controls.Add(table);
        Controls.Add(scroller);

        Controls.Add(new Label
        {
            Text = "A service pipak mentese utan a futo service automatikusan ujratolti a beallitasokat. Ures service cella = jelenleg nem service-kompatibilis.",
            Location = new Point(18, 520),
            Size = new Size(560, 36)
        });

        var save = new Button
        {
            Text = "Mentes",
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 228, ClientSize.Height - 48)
        };
        save.Click += (_, _) => Save();

        var cancel = new Button
        {
            Text = "Megse",
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 118, ClientSize.Height - 48)
        };
        cancel.Click += (_, _) => Close();

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(save);
        Controls.Add(cancel);
    }

    private void LoadValues()
    {
        _notificationsApp.Checked = _settings.MqttNotificationsEnabled;
        _mediaPlayerApp.Checked = _settings.MqttMediaPlayerEnabled;
        _systemSensorsService.Checked = _settings.MqttServiceSystemSensorsEnabled;
        _systemSensorsApp.Checked = _settings.MqttSystemSensorsEnabled;

        foreach (var row in _commandRows)
        {
            row.TrayApp.Checked = _settings.IsTrayAppCommandEnabled(row.Definition.Name);
            if (row.Service is not null)
            {
                row.Service.Checked = _settings.IsServiceCommandEnabled(row.Definition.Name);
            }
        }
    }

    private void Save()
    {
        _settings.MqttNotificationsEnabled = _notificationsApp.Checked;
        _settings.MqttMediaPlayerEnabled = _mediaPlayerApp.Checked;
        _settings.MqttServiceSystemSensorsEnabled = _systemSensorsService.Checked;
        _settings.MqttSystemSensorsEnabled = _systemSensorsApp.Checked;

        _settings.TrayAppCommands = _commandRows
            .Where(row => row.TrayApp.Checked)
            .Select(row => row.Definition.Name)
            .ToList();
        _settings.ServiceCommands = _commandRows
            .Where(row => row.Service is not null && row.Service.Checked && row.Definition.SupportsService)
            .Select(row => row.Definition.Name)
            .ToList();

        _settings.MqttButtonsEnabled = _settings.TrayAppCommands.Count > 0;

        SettingsStore.Save(_paths, _settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddHeader(TableLayoutPanel table, string text, int column)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        }, column, 0);
    }

    private static void AddFeatureRow(TableLayoutPanel table, int row, string label, CheckBox? serviceCheckBox, CheckBox trayCheckBox)
    {
        table.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 0),
            Margin = Padding.Empty
        }, 0, row);

        table.Controls.Add(WrapCheckBox(serviceCheckBox), 1, row);
        table.Controls.Add(WrapCheckBox(trayCheckBox), 2, row);
    }

    private static void AddSectionRow(TableLayoutPanel table, int row, string label)
    {
        var section = new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(235, 235, 235),
            Margin = Padding.Empty
        };

        table.Controls.Add(section, 0, row);
        table.SetColumnSpan(section, 3);
    }

    private static Panel WrapCheckBox(CheckBox? checkBox)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        if (checkBox is null)
        {
            panel.Controls.Add(new Label
            {
                Text = "-",
                ForeColor = SystemColors.GrayText,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            });
            return panel;
        }

        checkBox.AutoSize = true;
        checkBox.Location = new Point(41, 7);
        panel.Controls.Add(checkBox);
        return panel;
    }

    private static void AddRowStyle(TableLayoutPanel table)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
    }

    private sealed record CommandRow(SystemCommandDefinition Definition, CheckBox? Service, CheckBox TrayApp);
}
