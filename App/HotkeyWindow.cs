using System.Windows.Interop;
using verba_windows.Utilities;

namespace verba_windows.AppHost;

public sealed class HotkeyWindow : IDisposable
{
    private const int FirstHotkeyId = 0x5642;
    private const int SecondHotkeyId = 0x5643;
    private readonly HwndSource _source;
    private int _activeHotkeyId = FirstHotkeyId;
    private bool _isRegistered;

    public HotkeyWindow(HotkeyGesture shortcut)
    {
        _source = new HwndSource(new HwndSourceParameters("verba-hotkey")
        { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
        _source.AddHook(WndProc);
        Shortcut = shortcut;
        _isRegistered = Register(_activeHotkeyId, shortcut);
    }

    public event EventHandler? Pressed;
    public bool IsRegistered => _isRegistered;
    public HotkeyGesture Shortcut { get; private set; }

    public bool TryUpdate(HotkeyGesture shortcut)
    {
        if (!shortcut.IsValid) return false;
        if (_isRegistered && shortcut == Shortcut) return true;

        var nextId = _isRegistered && _activeHotkeyId == FirstHotkeyId ? SecondHotkeyId : FirstHotkeyId;
        if (!Register(nextId, shortcut)) return false;
        if (_isRegistered) NativeMethods.UnregisterHotKey(_source.Handle, _activeHotkeyId);
        _activeHotkeyId = nextId;
        Shortcut = shortcut;
        _isRegistered = true;
        return true;
    }

    private bool Register(int id, HotkeyGesture shortcut) => NativeMethods.RegisterHotKey(
        _source.Handle, id, shortcut.NativeModifiers, shortcut.VirtualKey);

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam == _activeHotkeyId)
        { handled = true; Pressed?.Invoke(this, EventArgs.Empty); }
        return 0;
    }

    public void Dispose()
    {
        if (_isRegistered) NativeMethods.UnregisterHotKey(_source.Handle, _activeHotkeyId);
        _source.RemoveHook(WndProc); _source.Dispose();
    }
}
