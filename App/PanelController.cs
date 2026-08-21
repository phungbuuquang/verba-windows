using System.Windows;
using verba_windows.Services;
using verba_windows.Utilities;
using verba_windows.ViewModels;

namespace verba_windows.AppHost;

public sealed class PanelController : IDisposable
{
    private readonly PanelWindow _window;
    private readonly TranslationViewModel _viewModel;
    private readonly ISelectionCapture _selection;
    private readonly SettingsStore _settings;
    private readonly HotkeyWindow _hotkey;
    private bool _opening;
    private bool _disposed;

    public PanelController(PanelWindow window, TranslationViewModel viewModel, ISelectionCapture selection, SettingsStore settings)
    {
        _window = window; _viewModel = viewModel; _selection = selection; _settings = settings;
        _window.HideRequested += (_, _) => Hide();
        _window.QuitRequested += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        var shortcut = HotkeyGesture.TryParse(settings.Shortcut, out var saved) ? saved : HotkeyGesture.Default;
        _hotkey = new HotkeyWindow(shortcut);
        _hotkey.Pressed += async (_, _) => await ToggleAsync();
        _window.ShortcutChangeRequested += OnShortcutChangeRequested;
        _window.SetShortcutState(_hotkey.Shortcut, _hotkey.IsRegistered);
    }

    public event EventHandler? QuitRequested;
    public event EventHandler<ShortcutEventArgs>? ShortcutChanged;
    public bool HotkeyRegistered => _hotkey.IsRegistered;
    public string ShortcutText => _hotkey.Shortcut.DisplayText;

    private void OnShortcutChangeRequested(object? sender, ShortcutEventArgs e)
    {
        if (!_hotkey.TryUpdate(e.Shortcut))
        {
            _window.SetShortcutState(_hotkey.Shortcut, _hotkey.IsRegistered, true);
            return;
        }
        _settings.SetShortcut(e.Shortcut.DisplayText);
        _window.SetShortcutState(e.Shortcut, true);
        ShortcutChanged?.Invoke(this, new ShortcutEventArgs(e.Shortcut));
        System.Diagnostics.Trace.WriteLine($"Global shortcut changed to {e.Shortcut.DisplayText}");
    }

    public async Task ToggleAsync()
    {
        if (_window.IsVisible) { Hide(); return; }
        await ShowAsync();
    }

    public async Task ShowAsync()
    {
        if (_opening || _disposed) return;
        _opening = true;
        try
        {
            var target = NativeMethods.GetForegroundWindow();
            string? selected = null;
            try { selected = await _selection.CaptureAsync(target); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Selection capture failed: {ex}"); }
            PositionWindow();
            _window.Show(); ClampToWorkingArea(); _window.Activate();
            System.Diagnostics.Trace.WriteLine($"Panel shown: visible={_window.IsVisible}, handle={new System.Windows.Interop.WindowInteropHelper(_window).Handle}");
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.TranslateExternalSelection(selected); _window.FocusInitial(true);
            }
            else _window.FocusInitial();
        }
        finally { _opening = false; }
    }

    public void ShowSelection(string selected)
    {
        if (_disposed || !_viewModel.TranslateExternalSelection(selected)) return;
        PositionWindow();
        _window.Show(); ClampToWorkingArea(); _window.Activate(); _window.FocusInitial(true);
    }

    public void Hide()
    {
        if (!_window.IsVisible) return;
        _viewModel.StopSpeech(); _settings.SetPanelPosition(_window.Left, _window.Top); _window.Hide();
    }

    private void PositionWindow()
    {
        var saved = _settings.PanelPosition;
        if (saved is { } p && IsOnAnyScreen(p.Left, p.Top, _window.Width, 520))
        { _window.Left = p.Left; _window.Top = p.Top; return; }
        var work = SystemParameters.WorkArea;
        _window.Left = Math.Max(work.Left + 8, work.Right - _window.Width - 8);
        _window.Top = Math.Max(work.Top + 8, work.Bottom - 560);
    }

    private static bool IsOnAnyScreen(double left, double top, double width, double height)
    {
        var rectangle = new System.Drawing.Rectangle((int)left, (int)top, Math.Max(1, (int)width), Math.Max(1, (int)height));
        return System.Windows.Forms.Screen.AllScreens.Any(x => x.WorkingArea.IntersectsWith(rectangle));
    }

    private void ClampToWorkingArea()
    {
        const double margin = 8;
        var rectangle = new System.Drawing.Rectangle((int)_window.Left, (int)_window.Top,
            Math.Max(1, (int)_window.ActualWidth), Math.Max(1, (int)_window.ActualHeight));
        var area = System.Windows.Forms.Screen.FromRectangle(rectangle).WorkingArea;
        var maxLeft = Math.Max(area.Left + margin, area.Right - _window.ActualWidth - margin);
        var maxTop = Math.Max(area.Top + margin, area.Bottom - _window.ActualHeight - margin);
        _window.Left = Math.Clamp(_window.Left, area.Left + margin, maxLeft);
        _window.Top = Math.Clamp(_window.Top, area.Top + margin, maxTop);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _hotkey.Dispose(); _viewModel.Dispose(); _window.Close();
    }
}
