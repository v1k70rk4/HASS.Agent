using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Configuration;
using HASS.Agent.Companion.Localization;
using HASS.Agent.Companion.Logging;
using HASS.Agent.Companion.Networking;
using HASS.Agent.Companion.Runtime;
using HASS.Agent.Companion.SystemCommands;
using HASS.Agent.Companion.SystemService;
using HASS.Agent.Companion.SystemStatus;

namespace HASS.Agent.Companion.Tray;

internal sealed class MainForm : Form
{
    private static readonly Color SidebarBg = Color.FromArgb(15, 23, 42);
    private static readonly Color SidebarHover = Color.FromArgb(30, 41, 59);
    private static readonly Color SidebarActiveBg = Color.FromArgb(30, 41, 59);
    private static readonly Color SidebarText = Color.FromArgb(148, 163, 184);
    private static readonly Color SidebarTextActive = Color.White;
    private static readonly Color Accent = Color.FromArgb(56, 189, 248);
    private static readonly Color PageBg = Color.FromArgb(241, 245, 249);
    private static readonly Color CardBg = Color.White;
    private static readonly Color BorderClr = Color.FromArgb(226, 232, 240);
    private static readonly Color TextDark = Color.FromArgb(15, 23, 42);
    private static readonly Color TextBody = Color.FromArgb(51, 65, 85);
    private static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color BtnBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color BtnBlueHover = Color.FromArgb(29, 78, 216);

    private readonly CompanionSettings _settings;
    private readonly AppPaths _paths;
    private readonly FileLog _log;

    private int _selectedPage;
    private readonly List<NavEntry> _nav = [];
    private readonly List<Panel> _pages = [];
    private readonly Panel _content;

    private readonly TextBox _deviceName = new();
    private readonly TextBox _bindHost = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535 };
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _showStartup = new();
    private readonly ComboBox _langCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _haLangCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly CheckBox _mqttEnabled = new();
    private readonly TextBox _mqttHost = new();
    private readonly NumericUpDown _mqttPort = new() { Minimum = 1, Maximum = 65535 };
    private readonly TextBox _mqttUser = new();
    private readonly TextBox _mqttPass = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _mqttTls = new();
    private readonly CheckBox _mqttRetain = new();

    private readonly CheckBox _capNotify = new();
    private readonly CheckBox _capMedia = new();
    private readonly CheckBox _capSensorsService = new();
    private readonly CheckBox _capSensorsApp = new();
    private readonly NumericUpDown _sensorInterval = new() { Minimum = 5, Maximum = 3600 };
    private readonly List<CmdRow> _cmdRows = [];

    private readonly DataGridView _builtInGrid = new();
    private readonly DataGridView _customGrid = new();

    public event EventHandler? SettingsSaved;

    public MainForm(CompanionSettings settings, AppPaths paths, FileLog log)
    {
        _settings = settings;
        _paths = paths;
        _log = log;

        Text = AppIdentity.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(900, 620);
        BackColor = PageBg;
        Icon = LoadIcon();

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = SidebarBg };
        BuildSidebar(sidebar);

        var bottomBar = BuildBottomBar();

        _content = new Panel { Dock = DockStyle.Fill, BackColor = PageBg };

        Controls.Add(_content);
        Controls.Add(bottomBar);
        Controls.Add(sidebar);

        _content.Controls.Add(BuildGeneralPage());
        _content.Controls.Add(BuildMqttPage());
        _content.Controls.Add(BuildCapabilitiesPage());
        _content.Controls.Add(BuildSensorsPage());
        _content.Controls.Add(BuildServicePage());
        _content.Controls.Add(BuildAboutPage());

        LoadSettings();
        SelectPage(0);
    }

    private static string S(string key) => Strings.Get(key);

    private void BuildSidebar(Panel sidebar)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = SidebarBg };
        header.Controls.Add(new Label
        {
            Text = ".NET 10", Font = new Font("Segoe UI", 9F), ForeColor = Accent,
            Location = new Point(20, 46), AutoSize = true
        });
        header.Controls.Add(new Label
        {
            Text = "HASS.Agent", Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = SidebarTextActive, Location = new Point(20, 18), AutoSize = true
        });

        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(51, 65, 85) };

        var version = new Label
        {
            Text = $"v{_settings.SoftwareVersion}", Dock = DockStyle.Bottom, Height = 36,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleCenter
        };

        var navContainer = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBg };
        string[] navKeys = ["Nav.General", "Nav.Mqtt", "Nav.Capabilities", "Nav.Sensors", "Nav.Service", "Nav.About"];
        for (var i = 0; i < navKeys.Length; i++)
            navContainer.Controls.Add(CreateNavItem(S(navKeys[i]), i));

        sidebar.Controls.Add(navContainer);
        sidebar.Controls.Add(version);
        sidebar.Controls.Add(sep);
        sidebar.Controls.Add(header);
    }

    private Panel CreateNavItem(string text, int index)
    {
        var item = new Panel { Location = new Point(0, 8 + index * 44), Size = new Size(240, 44), BackColor = SidebarBg, Cursor = Cursors.Hand };
        var indicator = new Panel { Location = Point.Empty, Size = new Size(3, 44), BackColor = Color.Transparent };
        var label = new Label
        {
            Text = text, ForeColor = SidebarText, Font = new Font("Segoe UI", 10.5F),
            AutoSize = false, Location = new Point(22, 0), Size = new Size(214, 44),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
        };

        item.Controls.Add(indicator);
        item.Controls.Add(label);

        void onClick(object? s, EventArgs e) => SelectPage(index);
        item.Click += onClick;
        label.Click += onClick;

        void onEnter(object? s, EventArgs e) { if (_selectedPage != index) item.BackColor = SidebarHover; }
        void onLeave(object? s, EventArgs e)
        {
            if (!item.ClientRectangle.Contains(item.PointToClient(Cursor.Position)))
                if (_selectedPage != index) item.BackColor = SidebarBg;
        }
        item.MouseEnter += onEnter; item.MouseLeave += onLeave;
        label.MouseEnter += onEnter; label.MouseLeave += onLeave;
        indicator.MouseEnter += onEnter; indicator.MouseLeave += onLeave;

        _nav.Add(new NavEntry(item, indicator, label));
        return item;
    }

    private void SelectPage(int index)
    {
        _selectedPage = index;
        for (var i = 0; i < _nav.Count; i++)
        {
            var selected = i == index;
            _nav[i].Panel.BackColor = selected ? SidebarActiveBg : SidebarBg;
            _nav[i].Indicator.BackColor = selected ? Accent : Color.Transparent;
            _nav[i].Label.ForeColor = selected ? SidebarTextActive : SidebarText;
            _nav[i].Label.Font = new Font("Segoe UI", 10.5F, selected ? FontStyle.Bold : FontStyle.Regular);
        }
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visible = i == index;
    }

    private Panel BuildBottomBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = CardBg };
        bar.Paint += (_, e) => { using var p = new Pen(BorderClr); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };

        var save = MakePrimaryButton(S("Btn.Save"), 110, 36);
        save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        save.Click += (_, _) => SaveSettings();

        var close = MakeSecondaryButton(S("Btn.Close"), 100, 36);
        close.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        bar.Layout += (_, _) =>
        {
            close.Location = new Point(bar.ClientSize.Width - close.Width - 20, 10);
            save.Location = new Point(close.Left - save.Width - 10, 10);
        };
        close.Click += (_, _) => Close();

        bar.Controls.Add(save);
        bar.Controls.Add(close);
        return bar;
    }

    private Panel BuildGeneralPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("General.Title"));

        var card1 = MakeCard(page, 28, 64, 720, 320, S("General.Device"));
        var y = 44;
        y = AddField(card1, S("General.DeviceName"), _deviceName, y);
        y = AddField(card1, S("General.BindHost"), _bindHost, y);
        y = AddField(card1, S("General.Port"), _port, y, inputWidth: 120);
        y = AddCheck(card1, _autoStart, S("General.AutoStart"), y + 4);
        y = AddCheck(card1, _showStartup, S("General.ShowStartup"), y);
        y += 6;

        card1.Controls.Add(new Label
        {
            Text = S("General.Language"), Location = new Point(20, y + 4),
            Size = new Size(160, 22), ForeColor = TextBody
        });
        foreach (var lang in Strings.AvailableLanguages)
        {
            _langCombo.Items.Add(Strings.GetDisplayName(lang));
            _haLangCombo.Items.Add(Strings.GetDisplayName(lang));
        }
        _langCombo.Location = new Point(188, y);
        _langCombo.Size = new Size(180, 28);
        card1.Controls.Add(_langCombo);
        y += 34;

        card1.Controls.Add(new Label
        {
            Text = S("General.HaLanguage"), Location = new Point(20, y + 4),
            Size = new Size(160, 22), ForeColor = TextBody
        });
        _haLangCombo.Location = new Point(188, y);
        _haLangCombo.Size = new Size(180, 28);
        card1.Controls.Add(_haLangCombo);

        var card2 = MakeCard(page, 28, 404, 720, 150, S("General.Network"));
        var urls = string.Join(Environment.NewLine, NetworkInfo.GetLanUrls(_settings.Port));
        var urlBox = new TextBox
        {
            Text = urls, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(20, 44), Size = new Size(540, 52),
            BackColor = PageBg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9F)
        };
        card2.Controls.Add(urlBox);
        var copyBtn = MakeSecondaryButton(S("General.CopyUrl"), 120, 30);
        copyBtn.Location = new Point(20, 100);
        copyBtn.Click += (_, _) => Clipboard.SetText(NetworkInfo.GetPreferredLanUrl(_settings.Port));
        card2.Controls.Add(copyBtn);

        var card3 = MakeCard(page, 28, 574, 720, 120, S("General.Files"));
        card3.Controls.Add(new Label
        {
            Text = $"{S("General.Settings")}: {_paths.SettingsFile}", Location = new Point(20, 42),
            Size = new Size(660, 20), ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F)
        });
        card3.Controls.Add(new Label
        {
            Text = $"{S("General.Log")}: {_paths.LogFile}", Location = new Point(20, 60),
            Size = new Size(660, 20), ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F)
        });
        var openBtn = MakeSecondaryButton(S("General.OpenFolder"), 140, 28);
        openBtn.Location = new Point(20, 82);
        openBtn.Click += (_, _) => OpenFolder(_paths.ConfigDirectory);
        card3.Controls.Add(openBtn);

        return page;
    }

    private Panel BuildMqttPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("Mqtt.Title"));

        var card = MakeCard(page, 28, 56, 600, 308, S("Mqtt.Connection"));
        var y = 44;
        y = AddCheck(card, _mqttEnabled, S("Mqtt.Enable"), y);
        y += 6;
        y = AddField(card, S("Mqtt.BrokerHost"), _mqttHost, y);
        y = AddField(card, S("Mqtt.Port"), _mqttPort, y, inputWidth: 120);
        y = AddField(card, S("Mqtt.Username"), _mqttUser, y);
        y = AddField(card, S("Mqtt.Password"), _mqttPass, y);
        y += 4;
        y = AddCheck(card, _mqttTls, S("Mqtt.UseTls"), y);
        AddCheck(card, _mqttRetain, S("Mqtt.RetainDiscovery"), y);

        return page;
    }

    private Panel BuildCapabilitiesPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("Cap.Title"));

        var card1 = MakeCard(page, 28, 56, 600, 232, S("Cap.Functions"));
        var y = 44;
        y = AddCheck(card1, _capNotify, S("Cap.Notifications"), y);
        y = AddCheck(card1, _capMedia, S("Cap.MediaPlayer"), y);
        y = AddCheck(card1, _capSensorsService, S("Cap.SensorsService"), y);
        y = AddCheck(card1, _capSensorsApp, S("Cap.SensorsApp"), y);
        y += 6;
        AddField(card1, S("Cap.SensorInterval"), _sensorInterval, y, inputWidth: 100);
        card1.Controls.Add(new Label
        {
            Text = S("Cap.Seconds"), Location = new Point(260, y + 4), AutoSize = true, ForeColor = TextMuted
        });

        var cmdY = 300;
        var card2 = MakeCard(page, 28, cmdY, 600, 50 + SystemCommandCatalog.Commands.Count * 30 + 16, S("Cap.Commands"));
        y = 44;
        card2.Controls.Add(new Label { Text = "Tray", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Location = new Point(320, y), AutoSize = true });
        card2.Controls.Add(new Label { Text = "Service", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Location = new Point(410, y), AutoSize = true });
        y += 24;

        foreach (var cmd in SystemCommandCatalog.Commands)
        {
            card2.Controls.Add(new Label
            {
                Text = S($"Cmd.{cmd.Name}"), Location = new Point(20, y + 2),
                Size = new Size(280, 22), ForeColor = TextBody
            });

            var tray = new CheckBox { Location = new Point(324, y), AutoSize = true, Enabled = cmd.SupportsTrayApp };
            card2.Controls.Add(tray);

            CheckBox? service = null;
            if (cmd.SupportsService)
            {
                service = new CheckBox { Location = new Point(418, y), AutoSize = true };
                card2.Controls.Add(service);
            }
            else
            {
                card2.Controls.Add(new Label
                {
                    Text = "—", Location = new Point(420, y + 1), AutoSize = true, ForeColor = TextMuted
                });
            }

            _cmdRows.Add(new CmdRow(cmd, tray, service));
            y += 30;
        }

        return page;
    }

    private Panel BuildSensorsPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("Sensors.Title"));

        var tabs = new TabControl
        {
            Location = new Point(28, 56),
            Size = new Size(600, 420),
            Font = new Font("Segoe UI", 9.5F)
        };

        var builtInTab = new TabPage(S("Sensors.BuiltIn")) { BackColor = CardBg, Padding = new Padding(4) };
        SetupBuiltInGrid();
        _builtInGrid.Dock = DockStyle.Fill;
        builtInTab.Controls.Add(_builtInGrid);
        tabs.TabPages.Add(builtInTab);

        var customTab = new TabPage(S("Sensors.Custom")) { BackColor = CardBg, Padding = new Padding(4) };
        SetupCustomGrid();
        _customGrid.Dock = DockStyle.Fill;

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = CardBg };
        var addBtn = MakeSecondaryButton(S("Sensors.Add"), 110, 30);
        addBtn.Location = new Point(4, 6);
        addBtn.Click += (_, _) => _customGrid.Rows.Add(true, true, true, CustomSensorTypes.ProcessRunning, S("Sensors.NewSensor"), "notepad");

        var removeBtn = MakeSecondaryButton(S("Sensors.Remove"), 90, 30);
        removeBtn.Location = new Point(122, 6);
        removeBtn.Click += (_, _) =>
        {
            if (_customGrid.CurrentRow is { IsNewRow: false } row)
                _customGrid.Rows.Remove(row);
        };
        btnPanel.Controls.Add(addBtn);
        btnPanel.Controls.Add(removeBtn);

        customTab.Controls.Add(_customGrid);
        customTab.Controls.Add(btnPanel);
        tabs.TabPages.Add(customTab);

        page.Controls.Add(tabs);
        return page;
    }

    private void SetupBuiltInGrid()
    {
        StyleGrid(_builtInGrid);
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "TrayApp", HeaderText = "Tray", Width = 50 });
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Service", HeaderText = "Svc", Width = 50 });
        _builtInGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name", HeaderText = S("Sensors.Sensor"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 200, ReadOnly = true
        });
    }

    private void SetupCustomGrid()
    {
        StyleGrid(_customGrid);
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = S("Sensors.Active"), Width = 50 });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "TrayApp", HeaderText = "Tray", Width = 50 });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Service", HeaderText = "Svc", Width = 50 });
        var sensorTypes = new[] { CustomSensorTypes.ProcessRunning, CustomSensorTypes.ServiceStatus, CustomSensorTypes.DiskFree }
            .Select(t => new { Key = t, Display = S($"SensorType.{t}") }).ToArray();
        _customGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Type", HeaderText = S("Sensors.Type"), Width = 150,
            DataSource = sensorTypes, ValueMember = "Key", DisplayMember = "Display"
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = S("Sensors.Name"), Width = 130 });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Parameter", HeaderText = S("Sensors.Parameter"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 80
        });
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.ScrollBars = ScrollBars.Both;
        grid.BackgroundColor = CardBg;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = BorderClr;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = TextDark;
        grid.ColumnHeadersDefaultCellStyle.BackColor = PageBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    private Panel BuildServicePage()
    {
        var page = MakePage();
        AddPageTitle(page, S("Service.Title"));

        var statusLabel = new Label
        {
            Text = CompanionServiceManager.GetStatusText(),
            Location = new Point(20, 44), Size = new Size(540, 64),
            ForeColor = TextBody, Font = new Font("Segoe UI", 9.5F)
        };

        var card1 = MakeCard(page, 28, 56, 600, 160, S("Service.Status"));
        card1.Controls.Add(statusLabel);

        var refreshBtn = MakeSecondaryButton(S("Service.Refresh"), 100, 30);
        refreshBtn.Location = new Point(20, 116);
        refreshBtn.Click += (_, _) => statusLabel.Text = CompanionServiceManager.GetStatusText();
        card1.Controls.Add(refreshBtn);

        var card2 = MakeCard(page, 28, 228, 600, 140, S("Service.Actions"));

        var installBtn = MakePrimaryButton(S("Service.Install"), 170, 36);
        installBtn.Location = new Point(20, 48);
        installBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--install-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
        };
        card2.Controls.Add(installBtn);

        var startBtn = MakeSecondaryButton(S("Service.Start"), 100, 36);
        startBtn.Location = new Point(200, 48);
        startBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--start-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
        };
        card2.Controls.Add(startBtn);

        var stopBtn = MakeSecondaryButton(S("Service.Stop"), 100, 36);
        stopBtn.Location = new Point(310, 48);
        stopBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--stop-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
        };
        card2.Controls.Add(stopBtn);

        var uninstallBtn = MakeSecondaryButton(S("Service.Uninstall"), 110, 36);
        uninstallBtn.Location = new Point(20, 96);
        uninstallBtn.ForeColor = Color.FromArgb(185, 28, 28);
        uninstallBtn.Click += (_, _) =>
        {
            if (MessageBox.Show(S("Service.ConfirmUninstall"), AppIdentity.DisplayName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                CompanionServiceManager.RunElevated("--uninstall-service", _log);
                statusLabel.Text = CompanionServiceManager.GetStatusText();
            }
        };
        card2.Controls.Add(uninstallBtn);

        return page;
    }

    private Panel BuildAboutPage()
    {
        var page = MakePage();

        var card = MakeCard(page, 28, 56, 600, 200, null);

        card.Controls.Add(new Label
        {
            Text = AppIdentity.DisplayName, Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = TextDark, Location = new Point(28, 24), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = $"{S("About.Version")}: {_settings.SoftwareVersion}",
            ForeColor = TextBody, Location = new Point(28, 62), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = $"{S("About.Developer")}: {_settings.Manufacturer}",
            ForeColor = TextBody, Location = new Point(28, 86), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = S("About.Description"),
            ForeColor = TextMuted, Location = new Point(28, 120), AutoSize = true
        });

        var ghLink = new LinkLabel
        {
            Text = "GitHub", Location = new Point(28, 152), AutoSize = true,
            LinkColor = BtnBlue, ActiveLinkColor = BtnBlueHover
        };
        ghLink.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/LAB02-Research/HASS.Agent") { UseShellExecute = true }); }
            catch { }
        };
        card.Controls.Add(ghLink);

        return page;
    }

    private void LoadSettings()
    {
        _deviceName.Text = _settings.DeviceName;
        _bindHost.Text = _settings.BindHost;
        _port.Value = _settings.Port;
        _autoStart.Checked = _settings.AutoStartOnLogin || StartupManager.IsEnabled();
        _showStartup.Checked = _settings.ShowStartupNotification;

        var langIdx = Strings.AvailableLanguages.ToList().IndexOf(_settings.Language);
        _langCombo.SelectedIndex = langIdx >= 0 ? langIdx : 0;
        var haLangIdx = Strings.AvailableLanguages.ToList().IndexOf(_settings.HaLanguage);
        _haLangCombo.SelectedIndex = haLangIdx >= 0 ? haLangIdx : 0;

        _mqttEnabled.Checked = _settings.MqttEnabled;
        _mqttHost.Text = _settings.MqttHost;
        _mqttPort.Value = _settings.MqttPort;
        _mqttUser.Text = _settings.MqttUsername;
        _mqttPass.Text = _settings.GetMqttPassword();
        _mqttTls.Checked = _settings.MqttUseTls;
        _mqttRetain.Checked = _settings.MqttRetainDiscovery;

        _capNotify.Checked = _settings.MqttNotificationsEnabled;
        _capMedia.Checked = _settings.MqttMediaPlayerEnabled;
        _capSensorsService.Checked = _settings.MqttServiceSystemSensorsEnabled;
        _capSensorsApp.Checked = _settings.MqttSystemSensorsEnabled;
        _sensorInterval.Value = _settings.SystemSensorsIntervalSeconds;

        foreach (var row in _cmdRows)
        {
            row.TrayApp.Checked = _settings.IsTrayAppCommandEnabled(row.Def.Name);
            if (row.Service is not null)
                row.Service.Checked = _settings.IsServiceCommandEnabled(row.Def.Name);
        }

        foreach (var def in BuiltInSensorCatalog.Sensors)
        {
            var s = _settings.BuiltInSensors.FirstOrDefault(x => x.Key == def.Key);
            var ri = _builtInGrid.Rows.Add(s?.TrayApp ?? def.SupportsTrayApp, s?.Service ?? def.SupportsService, S($"Sensor.{def.Key}"));
            _builtInGrid.Rows[ri].Tag = def.Key;
            if (!def.SupportsTrayApp) { _builtInGrid.Rows[ri].Cells["TrayApp"].ReadOnly = true; _builtInGrid.Rows[ri].Cells["TrayApp"].Style.BackColor = SystemColors.Control; }
            if (!def.SupportsService) { _builtInGrid.Rows[ri].Cells["Service"].ReadOnly = true; _builtInGrid.Rows[ri].Cells["Service"].Style.BackColor = SystemColors.Control; }
        }

        foreach (var sensor in _settings.CustomSensors)
        {
            var ri = _customGrid.Rows.Add(sensor.Enabled, sensor.TrayApp, sensor.Service, sensor.Type, sensor.Name, sensor.Parameter);
            _customGrid.Rows[ri].Tag = sensor.Id;
        }
    }

    private void SaveSettings()
    {
        _settings.DeviceName = _deviceName.Text.Trim();
        _settings.BindHost = _bindHost.Text.Trim();
        _settings.Port = (int)_port.Value;
        _settings.AutoStartOnLogin = _autoStart.Checked;
        StartupManager.SetEnabled(_settings.AutoStartOnLogin);
        _settings.ShowStartupNotification = _showStartup.Checked;

        var selectedLang = Strings.AvailableLanguages[_langCombo.SelectedIndex >= 0 ? _langCombo.SelectedIndex : 0];
        var selectedHaLang = Strings.AvailableLanguages[_haLangCombo.SelectedIndex >= 0 ? _haLangCombo.SelectedIndex : 0];
        var langChanged = _settings.Language != selectedLang;
        _settings.Language = selectedLang;
        _settings.HaLanguage = selectedHaLang;
        Strings.Language = selectedLang;
        Strings.HaLanguage = selectedHaLang;

        _settings.MqttEnabled = _mqttEnabled.Checked;
        _settings.MqttHost = _mqttHost.Text.Trim();
        _settings.MqttPort = (int)_mqttPort.Value;
        _settings.MqttUsername = _mqttUser.Text.Trim();
        _settings.SetMqttPassword(_mqttPass.Text);
        _settings.MqttUseTls = _mqttTls.Checked;
        _settings.MqttRetainDiscovery = _mqttRetain.Checked;

        _settings.MqttNotificationsEnabled = _capNotify.Checked;
        _settings.MqttMediaPlayerEnabled = _capMedia.Checked;
        _settings.MqttServiceSystemSensorsEnabled = _capSensorsService.Checked;
        _settings.MqttSystemSensorsEnabled = _capSensorsApp.Checked;
        _settings.SystemSensorsIntervalSeconds = (int)_sensorInterval.Value;

        _settings.TrayAppCommands = _cmdRows
            .Where(r => r.TrayApp.Checked)
            .Select(r => r.Def.Name).ToList();
        _settings.ServiceCommands = _cmdRows
            .Where(r => r.Service is not null && r.Service.Checked && r.Def.SupportsService)
            .Select(r => r.Def.Name).ToList();
        _settings.MqttButtonsEnabled = _settings.TrayAppCommands.Count > 0;

        _builtInGrid.EndEdit();
        _settings.BuiltInSensors = _builtInGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => new BuiltInSensorSetting
            {
                Key = Convert.ToString(r.Tag) ?? string.Empty,
                Service = Convert.ToBoolean(r.Cells["Service"].Value ?? false),
                TrayApp = Convert.ToBoolean(r.Cells["TrayApp"].Value ?? false)
            }).ToList();

        _customGrid.EndEdit();
        var sensors = new List<CustomSensorDefinition>();
        foreach (DataGridViewRow row in _customGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var param = Convert.ToString(row.Cells["Parameter"].Value) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(param)) continue;
            var type = Convert.ToString(row.Cells["Type"].Value) ?? CustomSensorTypes.ProcessRunning;
            var name = Convert.ToString(row.Cells["Name"].Value) ?? string.Empty;
            sensors.Add(new CustomSensorDefinition
            {
                Id = Convert.ToString(row.Tag) ?? Guid.NewGuid().ToString("N"),
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
                Type = type,
                Name = string.IsNullOrWhiteSpace(name) ? type : name.Trim(),
                Parameter = param.Trim(),
                Service = Convert.ToBoolean(row.Cells["Service"].Value ?? false),
                TrayApp = Convert.ToBoolean(row.Cells["TrayApp"].Value ?? false)
            });
        }
        _settings.CustomSensors = sensors;

        SettingsStore.Save(_paths, _settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);

        var msg = langChanged
            ? $"{S("Msg.SettingsSaved")}\n\n{S("Msg.RestartRequired")}"
            : S("Msg.SettingsSaved");
        MessageBox.Show(msg, AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private Panel MakePage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = PageBg, AutoScroll = true, Visible = false };
        page.Layout += (_, _) =>
        {
            var w = Math.Min(page.ClientSize.Width - 56, 860);
            foreach (Control c in page.Controls)
            {
                if (c is Label) continue;
                c.Width = w;
            }
        };
        _pages.Add(page);
        return page;
    }

    private static void AddPageTitle(Panel page, string text)
    {
        page.Controls.Add(new Label
        {
            Text = text, Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = TextDark, Location = new Point(28, 16), AutoSize = true
        });
    }

    private Panel MakeCard(Panel page, int x, int y, int width, int height, string? title)
    {
        var card = new Panel
        {
            Location = new Point(x, y), Size = new Size(width, height),
            BackColor = CardBg
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderClr);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        if (title is not null)
        {
            card.Controls.Add(new Label
            {
                Text = title, Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextDark, Location = new Point(20, 14), AutoSize = true
            });
        }
        page.Controls.Add(card);
        return card;
    }

    private int AddField(Panel card, string label, Control input, int y, int labelWidth = 160, int inputWidth = 340)
    {
        card.Controls.Add(new Label
        {
            Text = label, Location = new Point(20, y + 4),
            Size = new Size(labelWidth, 22), ForeColor = TextBody
        });
        input.Location = new Point(28 + labelWidth, y);
        input.Size = new Size(inputWidth, 28);
        card.Controls.Add(input);
        return y + 34;
    }

    private static int AddCheck(Panel card, CheckBox cb, string text, int y)
    {
        cb.Text = text;
        cb.Location = new Point(20, y);
        cb.Size = new Size(Math.Max(420, card.ClientSize.Width - 40), 26);
        cb.ForeColor = TextBody;
        card.Controls.Add(cb);
        return y + 30;
    }

    private static Button MakePrimaryButton(string text, int w, int h)
    {
        var btn = new Button
        {
            Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
            BackColor = BtnBlue, ForeColor = Color.White, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = BtnBlueHover;
        return btn;
    }

    private static Button MakeSecondaryButton(string text, int w, int h)
    {
        var btn = new Button
        {
            Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
            BackColor = CardBg, ForeColor = TextBody, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        btn.FlatAppearance.BorderColor = BorderClr;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = PageBg;
        return btn;
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private static Icon? LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "hassagent.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : null;
    }

    private sealed record NavEntry(Panel Panel, Panel Indicator, Label Label);
    private sealed record CmdRow(SystemCommandDefinition Def, CheckBox TrayApp, CheckBox? Service);
}
