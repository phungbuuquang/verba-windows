using System.Drawing;
using Forms = System.Windows.Forms;

namespace verba_windows.AppHost;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripItem _open;

    public TrayIcon(PanelController controller)
    {
        using var iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppIcon.ico")).Stream;
        using var applicationIcon = new Icon(iconStream);

        var menu = new Forms.ContextMenuStrip();
        _open = menu.Items.Add(controller.HotkeyRegistered
            ? $"Open verba ({controller.ShortcutText})"
            : "Open verba");
        _open.Click += async (_, _) => await controller.ShowAsync();
        menu.Items.Add(new Forms.ToolStripSeparator());
        var quit = menu.Items.Add("Quit");
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _icon = new Forms.NotifyIcon
        {
            Text = controller.HotkeyRegistered ? $"verba — {controller.ShortcutText}" : "verba",
            Icon = (Icon)applicationIcon.Clone(), Visible = true, ContextMenuStrip = menu
        };
        _icon.BalloonTipTitle = "verba is running";
        _icon.BalloonTipText = controller.HotkeyRegistered
            ? $"Press {controller.ShortcutText} to open the translation panel."
            : "Open settings to choose an available global shortcut.";
        _icon.ShowBalloonTip(4000);
        _icon.MouseClick += async (_, e) => { if (e.Button == Forms.MouseButtons.Left) await controller.ToggleAsync(); };
        controller.ShortcutChanged += (_, e) => UpdateShortcut(e.Shortcut.DisplayText);
    }

    private void UpdateShortcut(string shortcut)
    {
        _open.Text = $"Open verba ({shortcut})";
        _icon.Text = $"verba — {shortcut}";
        _icon.BalloonTipText = $"Press {shortcut} to open the translation panel.";
    }

    public event EventHandler? QuitRequested;
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
