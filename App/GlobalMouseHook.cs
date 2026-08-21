using System.ComponentModel;
using System.Runtime.InteropServices;
using verba_windows.Utilities;

namespace verba_windows.AppHost;

internal enum GlobalMouseAction
{
    LeftDown,
    LeftUp,
    RightDown
}

internal sealed class GlobalMouseEventArgs(GlobalMouseAction action, System.Drawing.Point screenPoint) : EventArgs
{
    public GlobalMouseAction Action { get; } = action;
    public System.Drawing.Point ScreenPoint { get; } = screenPoint;
}

internal sealed class GlobalMouseHook : IDisposable
{
    private readonly NativeMethods.MouseHookProc _callback;
    private nint _hook;

    public GlobalMouseHook()
    {
        _callback = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public event EventHandler<GlobalMouseEventArgs>? MouseAction;

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var action = message.ToInt32() switch
            {
                NativeMethods.WmLButtonDown => GlobalMouseAction.LeftDown,
                NativeMethods.WmLButtonUp => GlobalMouseAction.LeftUp,
                NativeMethods.WmRButtonDown => GlobalMouseAction.RightDown,
                _ => (GlobalMouseAction?)null
            };
            if (action is { } value)
            {
                var input = Marshal.PtrToStructure<NativeMethods.LowLevelMouseInput>(data);
                try
                {
                    MouseAction?.Invoke(this,
                        new GlobalMouseEventArgs(value, new System.Drawing.Point(input.X, input.Y)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Global mouse handler failed: {ex}");
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;
        GC.KeepAlive(_callback);
    }
}
