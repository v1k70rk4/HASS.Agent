using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
    private readonly Label _generalMqttError = new();
    private readonly Label _generalServiceWarning = new();
    private Control? _generalDeviceCard;
    private Control? _generalNetworkCard;
    private Control? _generalFilesCard;
    private readonly Label _serviceWarning = new();
    private Control? _serviceStatusCard;
    private Control? _serviceActionsCard;

    private readonly CheckBox _mqttEnabled = new();
    private readonly Label _mqttWarning = new();
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
    private readonly NumericUpDown _fastSensorInterval = new() { Minimum = 10, Maximum = 3600 };
    private readonly NumericUpDown _normalSensorInterval = new() { Minimum = 10, Maximum = 86400 };
    private readonly NumericUpDown _hourlySensorInterval = new() { Minimum = 10, Maximum = 86400 };
    private readonly List<CmdRow> _cmdRows = [];

    private readonly DataGridView _builtInGrid = new();
    private readonly DataGridView _customGrid = new();

    public event EventHandler? SettingsSaved;

    public MainForm(CompanionSettings settings, AppPaths paths, FileLog log, int initialPage = 0)
    {
        _settings = settings;
        _paths = paths;
        _log = log;

        Text = AppIdentity.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = Sz(900, 600);
        MinimumSize = Sz(700, 480);
        BackColor = PageBg;
        Icon = LoadIcon();

        var sidebar = new Panel { Dock = DockStyle.Left, Width = D(200), BackColor = SidebarBg };
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
        SelectPage(initialPage);
    }

    public void NavigateToPage(int index) => SelectPage(index);

    // ── DPI helpers ────────────────────────────────────────────────
    // All layout values in this file are authored at 96 DPI (100%).
    // D() scales a single int, Pt()/Sz() scale a Point/Size.
    // Font sizes are in points and are NOT scaled (points are DPI-independent).

    private int D(int v) => (int)(v * DeviceDpi / 96f);
    private Point Pt(int x, int y) => new(D(x), D(y));
    private Size Sz(int w, int h) => new(D(w), D(h));

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var screen = Screen.FromControl(this).WorkingArea;
        if (Width > screen.Width || Height > screen.Height)
        {
            var w = Math.Min(Width, screen.Width);
            var h = Math.Min(Height, screen.Height);
            SetBounds(
                screen.X + (screen.Width - w) / 2,
                screen.Y + (screen.Height - h) / 2,
                w, h);
        }
    }

    private static string S(string key) => Strings.Get(key);

    // ── Sidebar ────────────────────────────────────────────────────

    private void BuildSidebar(Panel sidebar)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = D(72), BackColor = SidebarBg };
        header.Controls.Add(new Label
        {
            Text = ".NET 10", Font = new Font("Segoe UI", 9F), ForeColor = Accent,
            Location = Pt(20, 46), AutoSize = true
        });
        header.Controls.Add(new Label
        {
            Text = "HASS.Agent", Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = SidebarTextActive, Location = Pt(20, 18), AutoSize = true
        });

        var sep = new Panel { Dock = DockStyle.Top, Height = D(1), BackColor = Color.FromArgb(51, 65, 85) };

        var version = new Label
        {
            Text = $"v{_settings.SoftwareVersion}", Dock = DockStyle.Bottom, Height = D(36),
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
        var item = new Panel { Location = new Point(0, D(8 + index * 44)), Size = Sz(200, 44), BackColor = SidebarBg, Cursor = Cursors.Hand };
        var indicator = new Panel { Location = Point.Empty, Size = Sz(3, 44), BackColor = Color.Transparent };
        var label = new Label
        {
            Text = text, ForeColor = SidebarText, Font = new Font("Segoe UI", 10.5F),
            AutoSize = false, Location = Pt(22, 0), Size = Sz(174, 44),
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

    // ── Bottom bar ─────────────────────────────────────────────────

    private Panel BuildBottomBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = D(56), BackColor = CardBg };
        bar.Paint += (_, e) => { using var p = new Pen(BorderClr); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };

        var save = MakePrimaryButton(S("Btn.Save"), 110, 36);
        save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        save.Click += (_, _) => SaveSettings();

        var close = MakeSecondaryButton(S("Btn.Close"), 100, 36);
        close.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        bar.Layout += (_, _) =>
        {
            close.Location = new Point(bar.ClientSize.Width - close.Width - D(20), D(10));
            save.Location = new Point(close.Left - save.Width - D(10), D(10));
        };
        close.Click += (_, _) => Close();

        bar.Controls.Add(save);
        bar.Controls.Add(close);
        return bar;
    }

    // ── Pages ──────────────────────────────────────────────────────

    private Panel BuildGeneralPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("General.Title"));

        ConfigureStatusLabel(
            _generalMqttError,
            "⚠  " + S("General.MqttDisabledError"),
            Color.FromArgb(153, 27, 27),
            Color.White);
        ConfigureStatusLabel(
            _generalServiceWarning,
            "⚠  " + S("General.ServiceNotInstalledWarning"),
            Color.FromArgb(255, 243, 224),
            Color.FromArgb(180, 60, 0));
        page.Controls.Add(_generalMqttError);
        page.Controls.Add(_generalServiceWarning);
        page.Layout += (_, _) => LayoutGeneralStatusMessages();

        var card1 = MakeCard(page, 28, 64, 720, 320, S("General.Device"));
        _generalDeviceCard = card1;
        var y = 44;
        y = AddField(card1, S("General.DeviceName"), _deviceName, y);
        y = AddField(card1, S("General.BindHost"), _bindHost, y);
        y = AddField(card1, S("General.Port"), _port, y, inputWidth: 120);
        y = AddCheck(card1, _autoStart, S("General.AutoStart"), y + 4);
        y = AddCheck(card1, _showStartup, S("General.ShowStartup"), y);
        y += 6;

        card1.Controls.Add(new Label
        {
            Text = S("General.Language"), Location = Pt(20, y + 4),
            Size = Sz(160, 22), ForeColor = TextBody
        });
        foreach (var lang in Strings.AvailableLanguages)
        {
            _langCombo.Items.Add(Strings.GetDisplayName(lang));
            _haLangCombo.Items.Add(Strings.GetDisplayName(lang));
        }
        _langCombo.Location = Pt(188, y);
        _langCombo.Size = Sz(180, 28);
        card1.Controls.Add(_langCombo);
        y += 34;

        card1.Controls.Add(new Label
        {
            Text = S("General.HaLanguage"), Location = Pt(20, y + 4),
            Size = Sz(160, 22), ForeColor = TextBody
        });
        _haLangCombo.Location = Pt(188, y);
        _haLangCombo.Size = Sz(180, 28);
        card1.Controls.Add(_haLangCombo);

        var card2 = MakeCard(page, 28, 404, 720, 194, S("General.Network"));
        _generalNetworkCard = card2;
        var urls = string.Join(Environment.NewLine, NetworkInfo.GetLanUrls(_settings.Port));
        var urlBox = new TextBox
        {
            Text = urls, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Location = Pt(20, 44), Size = Sz(540, 52),
            BackColor = PageBg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9F)
        };
        card2.Controls.Add(urlBox);
        var copyBtn = MakeSecondaryButton(S("General.CopyUrl"), 120, 30);
        copyBtn.Location = Pt(20, 100);
        copyBtn.Click += (_, _) => Clipboard.SetText(NetworkInfo.GetPreferredLanUrl(_settings.Port));
        card2.Controls.Add(copyBtn);

        card2.Controls.Add(new Label
        {
            Text = S("General.ApiKey"), Location = Pt(20, 140),
            Size = Sz(80, 22), ForeColor = TextBody
        });
        var apiKeyBox = new TextBox
        {
            Text = _settings.ApiKey, ReadOnly = true,
            Location = Pt(104, 136), Size = Sz(400, 28),
            BackColor = PageBg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9F)
        };
        card2.Controls.Add(apiKeyBox);
        var copyKeyBtn = MakeSecondaryButton(S("General.CopyApiKey"), 100, 28);
        copyKeyBtn.Location = Pt(512, 136);
        copyKeyBtn.Click += (_, _) => Clipboard.SetText(_settings.ApiKey);
        card2.Controls.Add(copyKeyBtn);

        var card3 = MakeCard(page, 28, 618, 720, 120, S("General.Files"));
        _generalFilesCard = card3;
        card3.Controls.Add(new Label
        {
            Text = $"{S("General.Settings")}: {_paths.SettingsFile}", Location = Pt(20, 42),
            Size = Sz(660, 20), ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F)
        });
        card3.Controls.Add(new Label
        {
            Text = $"{S("General.Log")}: {_paths.LogFile}", Location = Pt(20, 60),
            Size = Sz(660, 20), ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5F)
        });
        var openBtn = MakeSecondaryButton(S("General.OpenFolder"), 140, 28);
        openBtn.Location = Pt(20, 82);
        openBtn.Click += (_, _) => OpenFolder(_paths.ConfigDirectory);
        card3.Controls.Add(openBtn);

        UpdateGeneralStatusMessages();
        return page;
    }

    private Panel BuildMqttPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("Mqtt.Title"));

        ConfigureStatusLabel(
            _mqttWarning,
            "⚠  " + S("Mqtt.NotConfigured"),
            Color.FromArgb(153, 27, 27),
            Color.White);
        _mqttWarning.Location = Pt(28, 56);
        _mqttWarning.Size = Sz(600, 34);
        _mqttWarning.Visible = !_settings.MqttEnabled;
        page.Controls.Add(_mqttWarning);

        var cardTop = _settings.MqttEnabled ? 56 : 96;
        var card = MakeCard(page, 28, cardTop, 600, 308, S("Mqtt.Connection"));
        var y = 44;
        y = AddCheck(card, _mqttEnabled, S("Mqtt.Enable"), y);
        _mqttEnabled.CheckedChanged += (_, _) =>
        {
            _mqttWarning.Visible = !_mqttEnabled.Checked;
            card.Location = Pt(28, _mqttEnabled.Checked ? 56 : 96);
        };
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

    private void ConfigureStatusLabel(Label label, string text, Color background, Color foreground)
    {
        label.Text = text;
        label.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        label.ForeColor = foreground;
        label.BackColor = background;
        label.BorderStyle = BorderStyle.None;
        label.AutoSize = false;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Padding = new Padding(D(12), 0, D(12), 0);
    }

    private void UpdateGeneralStatusMessages()
    {
        _generalMqttError.Visible = !_settings.MqttEnabled;
        _generalServiceWarning.Visible = !IsServiceInstalled();
        LayoutGeneralStatusMessages();
    }

    private void UpdateServiceStatusMessage()
    {
        _serviceWarning.Visible = !IsServiceInstalled();
        LayoutServiceStatusMessage();
    }

    private static bool IsServiceInstalled()
    {
        try
        {
            return CompanionServiceManager.IsInstalled();
        }
        catch
        {
            return false;
        }
    }

    private void LayoutGeneralStatusMessages()
    {
        var y = D(56);
        var statusWidth = _generalDeviceCard?.Width ?? D(720);
        foreach (var label in new[] { _generalMqttError, _generalServiceWarning })
        {
            if (!label.Visible)
            {
                continue;
            }

            label.Location = new Point(D(28), y);
            label.Size = new Size(statusWidth, D(34));
            label.BringToFront();
            y += D(42);
        }

        var firstCardTop = y == D(56) ? D(64) : y + D(8);
        if (_generalDeviceCard is not null)
        {
            _generalDeviceCard.Top = firstCardTop;
        }

        if (_generalNetworkCard is not null && _generalDeviceCard is not null)
        {
            _generalNetworkCard.Top = _generalDeviceCard.Bottom + D(20);
        }

        if (_generalFilesCard is not null && _generalNetworkCard is not null)
        {
            _generalFilesCard.Top = _generalNetworkCard.Bottom + D(20);
        }
    }

    private void LayoutServiceStatusMessage()
    {
        var firstCardTop = D(56);
        if (_serviceWarning.Visible)
        {
            _serviceWarning.Location = Pt(28, 56);
            _serviceWarning.Size = new Size(_serviceStatusCard?.Width ?? D(600), D(34));
            _serviceWarning.BringToFront();
            firstCardTop = D(98);
        }

        if (_serviceStatusCard is not null)
        {
            _serviceStatusCard.Top = firstCardTop;
        }

        if (_serviceActionsCard is not null && _serviceStatusCard is not null)
        {
            _serviceActionsCard.Top = _serviceStatusCard.Bottom + D(12);
        }
    }

    private Panel BuildCapabilitiesPage()
    {
        var page = MakePage();

        AddPageTitle(page, S("Cap.Title"));

        var card1 = MakeCard(page, 28, 56, 600, 300, S("Cap.Functions"));
        var y = 44;
        y = AddCheck(card1, _capNotify, S("Cap.Notifications"), y);
        y = AddCheck(card1, _capMedia, S("Cap.MediaPlayer"), y);
        y = AddCheck(card1, _capSensorsService, S("Cap.SensorsService"), y);
        y = AddCheck(card1, _capSensorsApp, S("Cap.SensorsApp"), y);
        y += 6;
        y = AddField(card1, S("Cap.FastSensorInterval"), _fastSensorInterval, y, inputWidth: 100);
        card1.Controls.Add(new Label
        {
            Text = S("Cap.Seconds"), Location = Pt(260, y - 30), AutoSize = true, ForeColor = TextMuted
        });
        y = AddField(card1, S("Cap.NormalSensorInterval"), _normalSensorInterval, y, inputWidth: 100);
        card1.Controls.Add(new Label
        {
            Text = S("Cap.Seconds"), Location = Pt(260, y - 30), AutoSize = true, ForeColor = TextMuted
        });
        y = AddField(card1, S("Cap.HourlySensorInterval"), _hourlySensorInterval, y, inputWidth: 100);
        card1.Controls.Add(new Label
        {
            Text = S("Cap.Seconds"), Location = Pt(260, y - 30), AutoSize = true, ForeColor = TextMuted
        });

        var cmdY = 368;
        var card2 = MakeCard(page, 28, cmdY, 600, 50 + SystemCommandCatalog.Commands.Count * 30 + 16, S("Cap.Commands"));
        y = 44;
        card2.Controls.Add(new Label { Text = "Tray", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Location = Pt(320, y), AutoSize = true });
        card2.Controls.Add(new Label { Text = "Service", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Location = Pt(410, y), AutoSize = true });
        y += 24;

        foreach (var cmd in SystemCommandCatalog.Commands)
        {
            card2.Controls.Add(new Label
            {
                Text = S($"Cmd.{cmd.Name}"), Location = Pt(20, y + 2),
                Size = Sz(280, 22), ForeColor = TextBody
            });

            var tray = new CheckBox { Location = Pt(324, y), AutoSize = true, Enabled = cmd.SupportsTrayApp };
            card2.Controls.Add(tray);

            CheckBox? service = null;
            if (cmd.SupportsService)
            {
                service = new CheckBox { Location = Pt(418, y), AutoSize = true };
                card2.Controls.Add(service);
            }
            else
            {
                card2.Controls.Add(new Label
                {
                    Text = "—", Location = Pt(420, y + 1), AutoSize = true, ForeColor = TextMuted
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
            Location = Pt(28, 56),
            Size = Sz(600, 420),
            Font = new Font("Segoe UI", 9.5F)
        };
        page.Layout += (_, _) =>
        {
            var hPad = D(28);
            var bottom = D(12);
            tabs.Width = Math.Max(D(400), page.ClientSize.Width - hPad * 2);
            tabs.Height = Math.Max(D(200), page.ClientSize.Height - tabs.Top - bottom);
        };

        var builtInTab = new TabPage(S("Sensors.BuiltIn")) { BackColor = CardBg, Padding = new Padding(D(4)) };
        SetupBuiltInGrid();
        _builtInGrid.Dock = DockStyle.Fill;
        builtInTab.Controls.Add(_builtInGrid);
        tabs.TabPages.Add(builtInTab);

        var customTab = new TabPage(S("Sensors.Custom")) { BackColor = CardBg, Padding = new Padding(D(4)) };
        SetupCustomGrid();
        _customGrid.Dock = DockStyle.Fill;

        var infoPanel = BuildCustomSensorInfoPanel();

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = D(40), BackColor = CardBg };
        var addBtn = MakeSecondaryButton(S("Sensors.Add"), 110, 30);
        addBtn.Location = Pt(4, 6);
        addBtn.Click += (_, _) =>
        {
            var rowIndex = AddCustomSensorRow(new CustomSensorDefinition
                {
                    Enabled = true,
                    TrayApp = true,
                    Service = true,
                    Type = CustomSensorTypes.ProcessRunning,
                    Name = S("Sensors.NewSensor"),
                    Parameter = "notepad",
                    PollingProfile = SensorPollingProfiles.ToKey(SensorPollingProfile.Normal)
                });
            MarkCustomSensorValueNotTested(_customGrid.Rows[rowIndex]);
        };

        var removeBtn = MakeSecondaryButton(S("Sensors.Remove"), 90, 30);
        removeBtn.Location = new Point(addBtn.Right + D(8), D(6));
        removeBtn.Click += (_, _) =>
        {
            if (_customGrid.CurrentRow is { IsNewRow: false } row)
                _customGrid.Rows.Remove(row);
        };
        var testBtn = MakeSecondaryButton(S("Sensors.TestValue"), 110, 30);
        testBtn.Location = new Point(removeBtn.Right + D(8), D(6));
        testBtn.Click += async (_, _) => await UpdateSelectedCustomSensorValueAsync();
        btnPanel.Controls.Add(addBtn);
        btnPanel.Controls.Add(removeBtn);
        btnPanel.Controls.Add(testBtn);

        _customGrid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 &&
                _customGrid.Columns[e.ColumnIndex].Name is "Type" or "Parameter")
            {
                MarkCustomSensorValueNotTested(_customGrid.Rows[e.RowIndex]);
            }
        };
        _customGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_customGrid.IsCurrentCellDirty)
            {
                _customGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _builtInGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0 ||
                _builtInGrid.Columns[e.ColumnIndex].Name != "Attributes" ||
                _builtInGrid.Rows[e.RowIndex].Tag is not string key ||
                BuiltInSensorCatalog.Find(key) is not { HasMultipleValues: true } definition)
            {
                return;
            }

            var rowIndexes = new List<int>();
            foreach (var attributePath in definition.AttributePaths ?? [])
            {
                rowIndexes.Add(AddCustomSensorRow(new CustomSensorDefinition
                {
                    Enabled = true,
                    TrayApp = definition.SupportsTrayApp,
                    Service = definition.SupportsService,
                    Type = CustomSensorTypes.BuiltInAttribute,
                    Name = string.Format(S("Sensors.AttributeSensorName"), S($"Sensor.{definition.Key}"), GetAttributeDisplayName(attributePath)),
                    Parameter = attributePath,
                    PollingProfile = SensorPollingProfiles.ToKey(definition.PollingProfile)
                }));
            }

            if (rowIndexes.Count == 0)
            {
                return;
            }

            tabs.SelectedTab = customTab;
            _customGrid.ClearSelection();
            _customGrid.Rows[rowIndexes[0]].Selected = true;
            _customGrid.CurrentCell = _customGrid.Rows[rowIndexes[0]].Cells["Parameter"];
            foreach (var rowIndex in rowIndexes)
            {
                MarkCustomSensorValueNotTested(_customGrid.Rows[rowIndex]);
            }
        };

        customTab.Controls.Add(_customGrid);
        customTab.Controls.Add(btnPanel);
        customTab.Controls.Add(infoPanel);
        tabs.TabPages.Add(customTab);

        page.Controls.Add(tabs);
        return page;
    }

    private void SetupBuiltInGrid()
    {
        StyleGrid(_builtInGrid);
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "TrayApp", HeaderText = "Tray", Width = D(50) });
        _builtInGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Service", HeaderText = "Svc", Width = D(50) });
        _builtInGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name", HeaderText = S("Sensors.Sensor"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = D(200), ReadOnly = true
        });
        _builtInGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Profile", HeaderText = S("Sensors.Profile"), Width = D(92), ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _builtInGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Attributes", HeaderText = string.Empty, Width = D(36), ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
    }

    private void SetupCustomGrid()
    {
        StyleGrid(_customGrid);
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = S("Sensors.Active"), Width = D(50) });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "TrayApp", HeaderText = "Tray", Width = D(50) });
        _customGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Service", HeaderText = "Svc", Width = D(50) });
        var sensorTypes = new[]
        {
            CustomSensorTypes.ProcessRunning,
            CustomSensorTypes.ServiceStatus,
            CustomSensorTypes.DiskFree,
            CustomSensorTypes.BuiltInAttribute
        }
            .Select(t => new KeyValuePair<string, string>(t, S($"SensorType.{t}")))
            .ToArray();
        _customGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Type", HeaderText = S("Sensors.Type"), Width = D(150),
            DataSource = sensorTypes, ValueMember = "Key", DisplayMember = "Value"
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = S("Sensors.Name"), Width = D(130) });
        _customGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Profile", HeaderText = S("Sensors.Profile"), Width = D(100),
            DataSource = BuildPollingProfileOptions(), ValueMember = "Key", DisplayMember = "Value"
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Parameter", HeaderText = S("Sensors.Parameter"),
            Width = D(170), MinimumWidth = D(80)
        });
        _customGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value", HeaderText = S("Sensors.Value"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = D(110), ReadOnly = true
        });
    }

    private Panel BuildCustomSensorInfoPanel()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = D(78), BackColor = Color.FromArgb(239, 246, 255) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(191, 219, 254));
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };

        panel.Controls.Add(new Label
        {
            Text = "i",
            Location = Pt(10, 10),
            Size = Sz(22, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = BtnBlue,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        });
        panel.Controls.Add(new Label
        {
            Text = S("Sensors.CustomHelp"),
            Location = Pt(40, 8),
            Size = Sz(520, 64),
            ForeColor = TextBody,
            Font = new Font("Segoe UI", 8.5F)
        });
        return panel;
    }

    private static string GetAttributeDisplayName(string attributePath)
    {
        var lastDot = attributePath.LastIndexOf('.');
        var name = lastDot >= 0 ? attributePath[(lastDot + 1)..] : attributePath;
        return name.Replace("[0]", string.Empty);
    }

    private KeyValuePair<string, string>[] BuildPollingProfileOptions()
    {
        return Enum.GetValues<SensorPollingProfile>()
            .Select(profile => new KeyValuePair<string, string>(
                SensorPollingProfiles.ToKey(profile),
                GetPollingProfileDisplayName(profile)))
            .ToArray();
    }

    private string GetPollingProfileDisplayName(SensorPollingProfile profile)
    {
        return S($"PollingProfile.{SensorPollingProfiles.ToKey(profile)}");
    }

    private void StyleGrid(DataGridView grid)
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
        grid.RowTemplate.Height = D(24);
    }

    private Panel BuildServicePage()
    {
        var page = MakePage();
        AddPageTitle(page, S("Service.Title"));

        ConfigureStatusLabel(
            _serviceWarning,
            "⚠  " + S("General.ServiceNotInstalledWarning"),
            Color.FromArgb(255, 243, 224),
            Color.FromArgb(180, 60, 0));
        page.Controls.Add(_serviceWarning);
        page.Layout += (_, _) => LayoutServiceStatusMessage();

        var statusLabel = new Label
        {
            Text = CompanionServiceManager.GetStatusText(),
            Location = Pt(20, 44), Size = Sz(540, 64),
            ForeColor = TextBody, Font = new Font("Segoe UI", 9.5F)
        };

        var card1 = MakeCard(page, 28, 56, 600, 160, S("Service.Status"));
        _serviceStatusCard = card1;
        card1.Controls.Add(statusLabel);

        var refreshBtn = MakeSecondaryButton(S("Service.Refresh"), 100, 30);
        refreshBtn.Location = Pt(20, 116);
        refreshBtn.Click += (_, _) =>
        {
            statusLabel.Text = CompanionServiceManager.GetStatusText();
            UpdateGeneralStatusMessages();
            UpdateServiceStatusMessage();
        };
        card1.Controls.Add(refreshBtn);

        var card2 = MakeCard(page, 28, 228, 600, 140, S("Service.Actions"));
        _serviceActionsCard = card2;

        var installBtn = MakePrimaryButton(S("Service.Install"), 170, 36);
        installBtn.Location = Pt(20, 48);
        installBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--install-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
            UpdateGeneralStatusMessages();
            UpdateServiceStatusMessage();
        };
        card2.Controls.Add(installBtn);

        var startBtn = MakeSecondaryButton(S("Service.Start"), 100, 36);
        startBtn.Location = Pt(200, 48);
        startBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--start-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
            UpdateGeneralStatusMessages();
            UpdateServiceStatusMessage();
        };
        card2.Controls.Add(startBtn);

        var stopBtn = MakeSecondaryButton(S("Service.Stop"), 100, 36);
        stopBtn.Location = Pt(310, 48);
        stopBtn.Click += (_, _) =>
        {
            CompanionServiceManager.RunElevated("--stop-service", _log);
            statusLabel.Text = CompanionServiceManager.GetStatusText();
            UpdateGeneralStatusMessages();
            UpdateServiceStatusMessage();
        };
        card2.Controls.Add(stopBtn);

        var uninstallBtn = MakeSecondaryButton(S("Service.Uninstall"), 110, 36);
        uninstallBtn.Location = Pt(20, 96);
        uninstallBtn.ForeColor = Color.FromArgb(185, 28, 28);
        uninstallBtn.Click += (_, _) =>
        {
            if (MessageBox.Show(S("Service.ConfirmUninstall"), AppIdentity.DisplayName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                CompanionServiceManager.RunElevated("--uninstall-service", _log);
                statusLabel.Text = CompanionServiceManager.GetStatusText();
                UpdateGeneralStatusMessages();
                UpdateServiceStatusMessage();
            }
        };
        card2.Controls.Add(uninstallBtn);

        UpdateServiceStatusMessage();
        return page;
    }

    private Panel BuildAboutPage()
    {
        var page = MakePage();

        var card = MakeCard(page, 28, 56, 600, 246, null);

        var icon = LoadIcon();
        if (icon is not null)
        {
            card.Controls.Add(new PictureBox
            {
                Image = icon.ToBitmap(),
                Location = Pt(28, 26),
                Size = Sz(72, 72),
                SizeMode = PictureBoxSizeMode.Zoom
            });
        }

        card.Controls.Add(new Label
        {
            Text = AppIdentity.DisplayName, Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = TextDark, Location = Pt(120, 24), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = $"{S("About.Version")}: {_settings.SoftwareVersion}",
            ForeColor = TextBody, Location = Pt(122, 62), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = $"{S("About.Developer")}: {_settings.Manufacturer}",
            ForeColor = TextBody, Location = Pt(122, 86), AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = S("About.Description"),
            ForeColor = TextMuted, Location = Pt(122, 120), AutoSize = true
        });

        var githubBtn = MakeSecondaryButton("GitHub", 110, 32);
        githubBtn.Location = Pt(28, 174);
        githubBtn.Click += (_, _) => OpenUrl(AppIdentity.GitHubRepositoryUrl);
        card.Controls.Add(githubBtn);

        var issueBtn = MakeSecondaryButton(S("About.ReportIssue"), 150, 32);
        issueBtn.Location = new Point(githubBtn.Right + D(8), D(174));
        issueBtn.Click += (_, _) => OpenUrl($"{AppIdentity.GitHubRepositoryUrl}/issues/new");
        card.Controls.Add(issueBtn);

        var updateBtn = MakePrimaryButton(S("About.CheckUpdates"), 160, 32);
        updateBtn.Location = new Point(issueBtn.Right + D(8), D(174));
        updateBtn.Click += async (_, _) => await CheckForUpdatesAsync(updateBtn);
        card.Controls.Add(updateBtn);

        return page;
    }

    // ── Settings load / save ───────────────────────────────────────

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
        _fastSensorInterval.Value = _settings.FastSensorIntervalSeconds;
        _normalSensorInterval.Value = _settings.NormalSensorIntervalSeconds;
        _hourlySensorInterval.Value = _settings.HourlySensorIntervalSeconds;

        foreach (var row in _cmdRows)
        {
            row.TrayApp.Checked = _settings.IsTrayAppCommandEnabled(row.Def.Name);
            if (row.Service is not null)
                row.Service.Checked = _settings.IsServiceCommandEnabled(row.Def.Name);
        }

        foreach (var def in BuiltInSensorCatalog.Sensors)
        {
            var s = _settings.BuiltInSensors.FirstOrDefault(x => x.Key == def.Key);
            var ri = _builtInGrid.Rows.Add(
                s?.TrayApp ?? def.SupportsTrayApp,
                s?.Service ?? def.SupportsService,
                S($"Sensor.{def.Key}"),
                GetPollingProfileDisplayName(def.PollingProfile),
                def.HasMultipleValues ? "+" : string.Empty);
            _builtInGrid.Rows[ri].Tag = def.Key;
            if (!def.SupportsTrayApp) { _builtInGrid.Rows[ri].Cells["TrayApp"].ReadOnly = true; _builtInGrid.Rows[ri].Cells["TrayApp"].Style.BackColor = SystemColors.Control; }
            if (!def.SupportsService) { _builtInGrid.Rows[ri].Cells["Service"].ReadOnly = true; _builtInGrid.Rows[ri].Cells["Service"].Style.BackColor = SystemColors.Control; }
            if (def.HasMultipleValues)
            {
                _builtInGrid.Rows[ri].Cells["Attributes"].ToolTipText = S("Sensors.MultiValueTooltip");
                _builtInGrid.Rows[ri].Cells["Attributes"].Style.ForeColor = BtnBlue;
            }
        }

        foreach (var sensor in _settings.CustomSensors)
        {
            AddCustomSensorRow(sensor);
        }
        MarkAllCustomSensorValuesNotTested();
    }

    private int AddCustomSensorRow(CustomSensorDefinition sensor)
    {
        var rowIndex = _customGrid.Rows.Add(
            sensor.Enabled,
            sensor.TrayApp,
            sensor.Service,
            sensor.Type,
            sensor.Name,
            SensorPollingProfiles.NormalizeKey(sensor.PollingProfile, SensorPollingProfile.Normal),
            sensor.Parameter);
        _customGrid.Rows[rowIndex].Tag = sensor.Id;
        return rowIndex;
    }

    private async Task UpdateSelectedCustomSensorValueAsync()
    {
        _customGrid.EndEdit();
        if (_customGrid.CurrentRow is { IsNewRow: false } row)
        {
            await UpdateCustomSensorRowValueAsync(row);
        }
    }

    private void MarkAllCustomSensorValuesNotTested()
    {
        if (!_customGrid.Columns.Contains("Value"))
        {
            return;
        }

        foreach (var row in _customGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow))
        {
            MarkCustomSensorValueNotTested(row);
        }
    }

    private void MarkCustomSensorValueNotTested(DataGridViewRow row)
    {
        if (row.IsNewRow || !_customGrid.Columns.Contains("Value"))
        {
            return;
        }

        var parameter = Convert.ToString(row.Cells["Parameter"].Value) ?? string.Empty;
        row.Cells["Value"].Value = string.IsNullOrWhiteSpace(parameter)
            ? S("Sensors.ValueMissingParameter")
            : S("Sensors.ValueNotTested");
    }

    private async Task UpdateCustomSensorRowValueAsync(DataGridViewRow row)
    {
        if (row.IsNewRow || !_customGrid.Columns.Contains("Value"))
        {
            return;
        }

        var valueCell = row.Cells["Value"];
        var parameter = Convert.ToString(row.Cells["Parameter"].Value) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(parameter))
        {
            valueCell.Value = S("Sensors.ValueMissingParameter");
            return;
        }

        valueCell.Value = S("Sensors.ValueLoading");
        try
        {
            var sensor = BuildCustomSensorFromRow(row, forceEnabled: true);
            var state = await Task.Run(() =>
            {
                using var metrics = new SystemMetricsService(_log, null);
                return metrics.Read([sensor], serviceRole: false).CustomSensors.FirstOrDefault();
            });
            if (row.DataGridView is null || row.DataGridView.IsDisposed)
            {
                return;
            }

            valueCell.Value = FormatSensorValue(state?.Value);
        }
        catch (Exception ex)
        {
            if (row.DataGridView is null || row.DataGridView.IsDisposed)
            {
                return;
            }

            valueCell.Value = string.Format(S("Sensors.ValueError"), ex.Message);
        }
    }

    private CustomSensorDefinition BuildCustomSensorFromRow(DataGridViewRow row, bool forceEnabled)
    {
        var type = Convert.ToString(row.Cells["Type"].Value) ?? CustomSensorTypes.ProcessRunning;
        var name = Convert.ToString(row.Cells["Name"].Value) ?? string.Empty;
        var parameter = Convert.ToString(row.Cells["Parameter"].Value) ?? string.Empty;
        var pollingProfile = Convert.ToString(row.Cells["Profile"].Value) ?? SensorPollingProfiles.ToKey(SensorPollingProfile.Normal);
        return new CustomSensorDefinition
        {
            Id = Convert.ToString(row.Tag) ?? Guid.NewGuid().ToString("N"),
            Enabled = forceEnabled || Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
            Type = type,
            Name = string.IsNullOrWhiteSpace(name) ? type : name.Trim(),
            Parameter = parameter.Trim(),
            PollingProfile = SensorPollingProfiles.NormalizeKey(pollingProfile, SensorPollingProfile.Normal),
            Service = forceEnabled || Convert.ToBoolean(row.Cells["Service"].Value ?? false),
            TrayApp = forceEnabled || Convert.ToBoolean(row.Cells["TrayApp"].Value ?? false)
        };
    }

    private static string FormatSensorValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private async Task CheckForUpdatesAsync(Button updateButton)
    {
        var originalText = updateButton.Text;
        updateButton.Enabled = false;
        updateButton.Text = S("About.CheckingUpdates");

        try
        {
            var update = await AppUpdateService.CheckAsync(_settings.SoftwareVersion);
            if (!string.IsNullOrWhiteSpace(update.Error))
            {
                MessageBox.Show(S("About.UpdateCheckFailed"), AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!update.UpdateAvailable)
            {
                MessageBox.Show(string.Format(S("About.NoUpdates"), update.LatestVersion), AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                string.Format(S("About.UpdateAvailable"), update.LatestVersion, update.InstalledVersion),
                AppIdentity.DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                OpenUrl(update.ReleaseUrl ?? $"{AppIdentity.GitHubRepositoryUrl}/releases/latest");
                return;
            }

            updateButton.Text = S("About.DownloadingUpdate");
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var targetPath = await AppUpdateService.DownloadAsync(update, downloads);

            var open = MessageBox.Show(
                string.Format(S("About.UpdateDownloaded"), targetPath),
                AppIdentity.DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(S("About.UpdateError"), ex.Message), AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            updateButton.Text = originalText;
            updateButton.Enabled = true;
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
        _settings.FastSensorIntervalSeconds = (int)_fastSensorInterval.Value;
        _settings.NormalSensorIntervalSeconds = (int)_normalSensorInterval.Value;
        _settings.HourlySensorIntervalSeconds = (int)_hourlySensorInterval.Value;

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
            var pollingProfile = Convert.ToString(row.Cells["Profile"].Value) ?? SensorPollingProfiles.ToKey(SensorPollingProfile.Normal);
            sensors.Add(new CustomSensorDefinition
            {
                Id = Convert.ToString(row.Tag) ?? Guid.NewGuid().ToString("N"),
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
                Type = type,
                Name = string.IsNullOrWhiteSpace(name) ? type : name.Trim(),
                Parameter = param.Trim(),
                PollingProfile = SensorPollingProfiles.NormalizeKey(pollingProfile, SensorPollingProfile.Normal),
                Service = Convert.ToBoolean(row.Cells["Service"].Value ?? false),
                TrayApp = Convert.ToBoolean(row.Cells["TrayApp"].Value ?? false)
            });
        }
        _settings.CustomSensors = sensors;

        SettingsStore.Save(_paths, _settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        UpdateGeneralStatusMessages();
        UpdateServiceStatusMessage();

        var msg = langChanged
            ? $"{S("Msg.SettingsSaved")}\n\n{S("Msg.RestartRequired")}"
            : S("Msg.SettingsSaved");
        MessageBox.Show(msg, AppIdentity.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Layout helpers ─────────────────────────────────────────────
    // Card/field y-values are in LOGICAL pixels (96 DPI).
    // The helpers convert to device pixels internally.

    private Panel MakePage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = PageBg, AutoScroll = true, Visible = false };
        page.Layout += (_, _) =>
        {
            var padding = D(56);
            var w = Math.Max(D(400), page.ClientSize.Width - padding);
            foreach (Control c in page.Controls)
            {
                if (c is Label) continue;
                c.Width = w;
            }
        };
        _pages.Add(page);
        return page;
    }

    private void AddPageTitle(Panel page, string text)
    {
        page.Controls.Add(new Label
        {
            Text = text, Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = TextDark, Location = Pt(28, 16), AutoSize = true
        });
    }

    /// <summary>All parameters are in logical (96 DPI) pixels.</summary>
    private Panel MakeCard(Panel page, int x, int y, int width, int height, string? title)
    {
        var card = new Panel
        {
            Location = Pt(x, y), Size = Sz(width, height),
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
                ForeColor = TextDark, Location = Pt(20, 14), AutoSize = true
            });
        }
        page.Controls.Add(card);
        return card;
    }

    /// <summary>y is logical. Returns the next logical y.</summary>
    private int AddField(Panel card, string label, Control input, int y, int labelWidth = 160, int inputWidth = 340)
    {
        card.Controls.Add(new Label
        {
            Text = label, Location = Pt(20, y + 4),
            Size = Sz(labelWidth, 22), ForeColor = TextBody
        });
        input.Location = Pt(28 + labelWidth, y);
        input.Size = Sz(inputWidth, 28);
        card.Controls.Add(input);
        return y + 34;
    }

    /// <summary>y is logical. Returns the next logical y.</summary>
    private int AddCheck(Panel card, CheckBox cb, string text, int y)
    {
        cb.Text = text;
        cb.Location = Pt(20, y);
        cb.Size = new Size(Math.Max(D(420), card.ClientSize.Width - D(40)), D(26));
        cb.ForeColor = TextBody;
        card.Controls.Add(cb);
        return y + 30;
    }

    /// <summary>w and h are logical (96 DPI) pixels.</summary>
    private Button MakePrimaryButton(string text, int w, int h)
    {
        var btn = new Button
        {
            Text = text, Size = Sz(w, h), FlatStyle = FlatStyle.Flat,
            BackColor = BtnBlue, ForeColor = Color.White, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = BtnBlueHover;
        return btn;
    }

    /// <summary>w and h are logical (96 DPI) pixels.</summary>
    private Button MakeSecondaryButton(string text, int w, int h)
    {
        var btn = new Button
        {
            Text = text, Size = Sz(w, h), FlatStyle = FlatStyle.Flat,
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

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
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
