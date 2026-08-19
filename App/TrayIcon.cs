using System.ComponentModel;
using System.Drawing;
using verba_windows.Services;
using Forms = System.Windows.Forms;

namespace verba_windows.AppHost;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripItem _open;
    private readonly Forms.ToolStripItem? _checkForUpdates;
    private readonly Forms.ToolStripItem? _applyUpdate;
    private readonly Forms.ToolStripItem _quit;
    private readonly PanelController _controller;
    private readonly AppLanguageStore _language;
    private string _shortcut;
    private string? _readyVersion;

    public TrayIcon(PanelController controller, AppLanguageStore language, bool updatesAvailable)
    {
        _controller = controller;
        _language = language;
        _shortcut = controller.ShortcutText;
        using var iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppIcon.ico")).Stream;
        using var applicationIcon = new Icon(iconStream);

        var menu = new Forms.ContextMenuStrip();
        _open = menu.Items.Add(OpenText());
        _open.Click += async (_, _) => await controller.ShowAsync();
        menu.Items.Add(new Forms.ToolStripSeparator());
        if (updatesAvailable)
        {
            _checkForUpdates = menu.Items.Add(language.Strings.CheckForUpdates);
            _checkForUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
            _applyUpdate = menu.Items.Add(language.Strings.RestartToUpdate);
            _applyUpdate.Visible = false;
            _applyUpdate.Click += (_, _) => ApplyUpdateRequested?.Invoke(this, EventArgs.Empty);
            menu.Items.Add(new Forms.ToolStripSeparator());
        }
        _quit = menu.Items.Add(language.Strings.Quit);
        _quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _icon = new Forms.NotifyIcon
        {
            Text = controller.HotkeyRegistered ? $"verba - {controller.ShortcutText}" : "verba",
            Icon = (Icon)applicationIcon.Clone(), Visible = true, ContextMenuStrip = menu
        };
        _icon.BalloonTipTitle = "verba is running";
        _icon.BalloonTipText = controller.HotkeyRegistered
            ? $"Press {controller.ShortcutText} to open the translation panel."
            : "Open settings to choose an available global shortcut.";
        _icon.ShowBalloonTip(4000);
        _icon.MouseClick += async (_, e) => { if (e.Button == Forms.MouseButtons.Left) await controller.ToggleAsync(); };
        controller.ShortcutChanged += (_, e) => UpdateShortcut(e.Shortcut.DisplayText);
        language.PropertyChanged += OnLanguageChanged;
    }

    private void UpdateShortcut(string shortcut)
    {
        _shortcut = shortcut;
        _open.Text = OpenText();
        _icon.Text = $"verba - {shortcut}";
        _icon.BalloonTipText = $"Press {shortcut} to open the translation panel.";
    }

    private string OpenText() => _controller.HotkeyRegistered
        ? _language.Strings.OpenVerbaWithShortcut(_shortcut)
        : _language.Strings.OpenVerba;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppLanguageStore.Strings)) return;
        _open.Text = OpenText();
        _quit.Text = _language.Strings.Quit;
        if (_checkForUpdates is not null && _readyVersion is null)
            _checkForUpdates.Text = _language.Strings.CheckForUpdates;
        if (_applyUpdate is not null)
            _applyUpdate.Text = _readyVersion is null
                ? _language.Strings.RestartToUpdate
                : _language.Strings.RestartToUpdateVersion(_readyVersion);
    }

    public void SetCheckingForUpdates()
    {
        if (_checkForUpdates is null) return;
        _checkForUpdates.Enabled = false;
        _checkForUpdates.Text = _language.Strings.CheckingForUpdates;
    }

    public void SetDownloadingUpdate(string version)
    {
        if (_checkForUpdates is null) return;
        _checkForUpdates.Enabled = false;
        _checkForUpdates.Text = _language.Strings.DownloadingUpdate(version);
    }

    public void SetUpdateReady(string version)
    {
        _readyVersion = version;
        if (_checkForUpdates is not null) _checkForUpdates.Visible = false;
        if (_applyUpdate is not null)
        {
            _applyUpdate.Text = _language.Strings.RestartToUpdateVersion(version);
            _applyUpdate.Visible = true;
        }
        ShowBalloon(_language.Strings.UpdateReadyTitle, _language.Strings.UpdateReadyMessage(version));
    }

    public void ShowUpToDate()
    {
        ResetUpdateStatus();
        ShowBalloon("verba", _language.Strings.UpToDate);
    }

    public void ShowUpdateCheckFailed()
    {
        ResetUpdateStatus();
        ShowBalloon("verba", _language.Strings.UpdateCheckFailed);
    }

    public void ResetUpdateStatus()
    {
        if (_checkForUpdates is null) return;
        _checkForUpdates.Enabled = true;
        _checkForUpdates.Text = _language.Strings.CheckForUpdates;
    }

    private void ShowBalloon(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(5000);
    }

    public event EventHandler? QuitRequested;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? ApplyUpdateRequested;

    public void Dispose()
    {
        _language.PropertyChanged -= OnLanguageChanged;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
