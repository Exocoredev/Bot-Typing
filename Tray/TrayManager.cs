using System.Drawing;
using System.Windows.Forms;

namespace AutoInputTester.Tray;

public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;

    public event Action? SelectAreaRequested;
    public event Action? PreviewRequested;
    public event Action? StartRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action? SettingsRequested;
    public event Action? LogsRequested;
    public event Action? ExitRequested;

    public TrayManager()
    {
        _menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Status: Bereit") { Enabled = false };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Bereich auswählen", null, (_, _) => SelectAreaRequested?.Invoke());
        _menu.Items.Add("OCR-Vorschau", null, (_, _) => PreviewRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Start", null, (_, _) => StartRequested?.Invoke());
        _menu.Items.Add("Pause / Fortsetzen", null, (_, _) => PauseRequested?.Invoke());
        _menu.Items.Add("Stop", null, (_, _) => StopRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Einstellungen", null, (_, _) => SettingsRequested?.Invoke());
        _menu.Items.Add("Testprotokolle öffnen", null, (_, _) => LogsRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Text = "Auto Input Tester",
            Icon = SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public void SetStatus(string text)
    {
        _statusItem.Text = $"Status: {text}";
        var tooltip = $"Auto Input Tester - {text}";
        _icon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip = null;
        _icon.Dispose();
        _menu.Dispose();
    }
}
