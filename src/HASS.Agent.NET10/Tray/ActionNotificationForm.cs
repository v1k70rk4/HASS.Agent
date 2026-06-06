using System.Drawing;
using System.Windows.Forms;
using HASS.Agent.Companion.Http;
using HASS.Agent.Companion.Runtime;

namespace HASS.Agent.Companion.Tray;

internal sealed class ActionNotificationForm : Form
{
    private static readonly Color SurfaceColor = Color.FromArgb(28, 31, 34);
    private static readonly Color BorderColor = Color.FromArgb(64, 68, 74);
    private static readonly Color PrimaryTextColor = Color.White;
    private static readonly Color SecondaryTextColor = Color.FromArgb(226, 229, 233);
    private static readonly Color ButtonColor = Color.FromArgb(52, 57, 63);
    private static readonly Color ButtonHoverColor = Color.FromArgb(66, 72, 80);
    private static readonly Color ButtonBorderColor = Color.FromArgb(96, 104, 114);
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);

    private readonly Action<string> _actionSelected;
    private readonly System.Windows.Forms.Timer _timer = new();

    public ActionNotificationForm(NotificationPayload notification, Action<string> actionSelected)
    {
        _actionSelected = actionSelected;

        Text = AppIdentity.DisplayName;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = SurfaceColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(420, CalculateHeight(notification));
        Padding = new Padding(0);

        AddTitle(notification);
        AddMessage(notification);
        AddActionButtons(notification);
        AddDismissButton();

        PositionFromBottom(16);

        _timer.Interval = notification.TimeoutMilliseconds;
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    public void PositionFromBottom(int bottomOffset)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - bottomOffset);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(BorderColor);
        e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        using var accent = new SolidBrush(AccentColor);
        e.Graphics.FillRectangle(accent, 0, 0, 4, ClientSize.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AddTitle(NotificationPayload notification)
    {
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(18, 16),
            Size = new Size(ClientSize.Width - 36, 24),
            Text = string.IsNullOrWhiteSpace(notification.Title) ? "Home Assistant" : notification.Title.Trim(),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = PrimaryTextColor,
            BackColor = BackColor,
            AutoEllipsis = true
        });
    }

    private void AddMessage(NotificationPayload notification)
    {
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(18, 46),
            Size = new Size(ClientSize.Width - 36, 56),
            Text = notification.Message?.Trim(),
            ForeColor = SecondaryTextColor,
            BackColor = BackColor
        });
    }

    private void AddActionButtons(NotificationPayload notification)
    {
        var x = 18;
        var y = 114;

        foreach (var action in notification.Actions.Take(5))
        {
            var button = CreateActionButton(action);
            if (x + button.Width > ClientSize.Width - 18)
            {
                x = 18;
                y += 42;
            }

            button.Location = new Point(x, y);
            Controls.Add(button);

            x += button.Width + 8;
        }
    }

    private void AddDismissButton()
    {
        var dismiss = new Button
        {
            Text = Localization.Strings.Get("Btn.Close"),
            Size = new Size(92, 32),
            Location = new Point(18, ClientSize.Height - 46)
        };
        StyleButton(dismiss, isPrimary: false);
        dismiss.Click += (_, _) => Close();
        Controls.Add(dismiss);
    }

    private Button CreateActionButton(NotificationActionPayload action)
    {
        var actionName = action.Action!.Trim();
        var title = string.IsNullOrWhiteSpace(action.Title) ? actionName : action.Title.Trim();

        var button = new Button
        {
            Text = title,
            Size = new Size(Math.Clamp(TextRenderer.MeasureText(title, Font).Width + 34, 92, 170), 34)
        };
        StyleButton(button, isPrimary: true);

        button.Click += (_, _) =>
        {
            _timer.Stop();
            _actionSelected(actionName);
            Close();
        };

        return button;
    }

    private static void StyleButton(Button button, bool isPrimary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = isPrimary ? ButtonColor : Color.FromArgb(40, 44, 49);
        button.ForeColor = PrimaryTextColor;
        button.FlatAppearance.BorderColor = isPrimary ? ButtonBorderColor : Color.FromArgb(72, 78, 86);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = isPrimary ? ButtonHoverColor : Color.FromArgb(52, 57, 63);
        button.FlatAppearance.MouseDownBackColor = AccentColor;
        button.Font = new Font("Segoe UI", 9F, isPrimary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }

    private static int CalculateHeight(NotificationPayload notification)
    {
        var actionRows = Math.Max(1, (int)Math.Ceiling(Math.Min(notification.Actions.Count, 5) / 2.0));
        return Math.Clamp(168 + actionRows * 42, 236, 360);
    }
}
