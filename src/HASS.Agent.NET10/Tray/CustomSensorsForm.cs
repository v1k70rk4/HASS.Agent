using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Localization;
using HASS.Agent.Companion.Runtime;
using HASS.Agent.Companion.SystemStatus;

namespace HASS.Agent.Companion.Tray;

internal sealed class CustomSensorsForm : Form
{
    private readonly CompanionSettings _settings;
    private readonly AppPaths _paths;
    private readonly DataGridView _builtInGrid = new();
    private readonly DataGridView _customGrid = new();

    public event EventHandler? SettingsSaved;

    public CustomSensorsForm(CompanionSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;

        Text = $"{AppIdentity.DisplayName} - {Strings.Get("Sensors.WindowTitle")}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(820, 480);

        BuildForm();
        LoadValues();
    }

    private void BuildForm()
    {
        Controls.Add(new Label
        {
            Text = Strings.Get("Sensors.Title"),
            Location = new Point(18, 14),
            Size = new Size(760, 24),
            Font = new Font(Font, FontStyle.Bold)
        });

        var tabs = new TabControl
        {
            Location = new Point(18, 48),
            Size = new Size(ClientSize.Width - 36, ClientSize.Height - 120),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        var builtInPage = new TabPage(Strings.Get("Sensors.Basic"));
        var customPage = new TabPage(Strings.Get("Sensors.Custom"));
        SetupBuiltInGrid();
        SetupCustomGrid();
        builtInPage.Controls.Add(_builtInGrid);
        customPage.Controls.Add(_customGrid);
        tabs.TabPages.Add(builtInPage);
        tabs.TabPages.Add(customPage);
        Controls.Add(tabs);

        var add = new Button
        {
            Text = Strings.Get("Sensors.CustomAdd"),
            Size = new Size(130, 34),
            Location = new Point(18, ClientSize.Height - 52),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        add.Click += (_, _) => AddCustomRow();

        var remove = new Button
        {
            Text = Strings.Get("Sensors.CustomRemove"),
            Size = new Size(110, 34),
            Location = new Point(158, ClientSize.Height - 52),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        remove.Click += (_, _) => RemoveSelectedCustomRow();

        var save = new Button
        {
            Text = Strings.Get("Btn.Save"),
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 228, ClientSize.Height - 52),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        save.Click += (_, _) => Save();

        var cancel = new Button
        {
            Text = Strings.Get("Btn.Cancel"),
            Size = new Size(100, 34),
            Location = new Point(ClientSize.Width - 118, ClientSize.Height - 52),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        cancel.Click += (_, _) => Close();

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(add);
        Controls.Add(remove);
        Controls.Add(save);
        Controls.Add(cancel);
    }

    private void SetupBuiltInGrid()
    {
        SetupGrid(_builtInGrid);
        _builtInGrid.Dock = DockStyle.Fill;
        _builtInGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = Strings.Get("Sensors.Name"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Service",
            HeaderText = "Service",
            Width = 80
        });
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "TrayApp",
            HeaderText = "Tray app",
            Width = 90
        });
    }

    private void SetupCustomGrid()
    {
        SetupGrid(_customGrid);
        _customGrid.Dock = DockStyle.Fill;
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = Strings.Get("Sensors.Active"),
            Width = 60
        });
        _customGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Type",
            HeaderText = Strings.Get("Sensors.Type"),
            Width = 150,
            DataSource = new[]
            {
                CustomSensorTypes.ProcessRunning,
                CustomSensorTypes.ServiceStatus,
                CustomSensorTypes.DiskFree
            }
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = Strings.Get("Sensors.Name"),
            Width = 180
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Parameter",
            HeaderText = Strings.Get("Sensors.Parameter"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Service",
            HeaderText = "Service",
            Width = 70
        });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "TrayApp",
            HeaderText = "Tray app",
            Width = 80
        });
    }

    private static void SetupGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
    }

    private void LoadValues()
    {
        foreach (var definition in BuiltInSensorCatalog.Sensors)
        {
            var setting = _settings.BuiltInSensors.First(sensor => sensor.Key == definition.Key);
            var rowIndex = _builtInGrid.Rows.Add(Strings.Get($"Sensor.{definition.Key}"), setting.Service, setting.TrayApp);
            _builtInGrid.Rows[rowIndex].Tag = definition.Key;
            _builtInGrid.Rows[rowIndex].Cells["Service"].ReadOnly = !definition.SupportsService;
            _builtInGrid.Rows[rowIndex].Cells["TrayApp"].ReadOnly = !definition.SupportsTrayApp;
            if (!definition.SupportsService)
            {
                _builtInGrid.Rows[rowIndex].Cells["Service"].Style.BackColor = SystemColors.Control;
            }
            if (!definition.SupportsTrayApp)
            {
                _builtInGrid.Rows[rowIndex].Cells["TrayApp"].Style.BackColor = SystemColors.Control;
            }
        }

        foreach (var sensor in _settings.CustomSensors)
        {
            AddCustomRow(sensor);
        }
    }

    private void AddCustomRow()
    {
        AddCustomRow(new CustomSensorDefinition
        {
            Name = Strings.Get("Sensors.NewSensor"),
            Parameter = "notepad",
            Type = CustomSensorTypes.ProcessRunning,
            Enabled = true,
            Service = true,
            TrayApp = true
        });
    }

    private void AddCustomRow(CustomSensorDefinition sensor)
    {
        var rowIndex = _customGrid.Rows.Add(sensor.Enabled, sensor.Type, sensor.Name, sensor.Parameter, sensor.Service, sensor.TrayApp);
        _customGrid.Rows[rowIndex].Tag = sensor.Id;
    }

    private void RemoveSelectedCustomRow()
    {
        if (_customGrid.CurrentRow is not null && !_customGrid.CurrentRow.IsNewRow)
        {
            _customGrid.Rows.Remove(_customGrid.CurrentRow);
        }
    }

    private void Save()
    {
        _builtInGrid.EndEdit();
        _customGrid.EndEdit();

        _settings.BuiltInSensors = _builtInGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => new BuiltInSensorSetting
            {
                Key = Convert.ToString(row.Tag) ?? string.Empty,
                Service = Convert.ToBoolean(row.Cells["Service"].Value ?? false),
                TrayApp = Convert.ToBoolean(row.Cells["TrayApp"].Value ?? false)
            })
            .ToList();

        var sensors = new List<CustomSensorDefinition>();
        foreach (DataGridViewRow row in _customGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var type = Convert.ToString(row.Cells["Type"].Value) ?? CustomSensorTypes.ProcessRunning;
            var name = Convert.ToString(row.Cells["Name"].Value) ?? string.Empty;
            var parameter = Convert.ToString(row.Cells["Parameter"].Value) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(parameter))
            {
                continue;
            }

            sensors.Add(new CustomSensorDefinition
            {
                Id = Convert.ToString(row.Tag) ?? Guid.NewGuid().ToString("N"),
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
                Type = type,
                Name = string.IsNullOrWhiteSpace(name) ? type : name.Trim(),
                Parameter = parameter.Trim(),
                Service = Convert.ToBoolean(row.Cells["Service"].Value ?? false),
                TrayApp = Convert.ToBoolean(row.Cells["TrayApp"].Value ?? false)
            });
        }

        _settings.CustomSensors = sensors;
        SettingsStore.Save(_paths, _settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);

        DialogResult = DialogResult.OK;
        Close();
    }
}
