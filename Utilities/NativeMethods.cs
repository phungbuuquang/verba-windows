using System.Runtime.InteropServices;

namespace verba_windows.Utilities;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WmHotkey = 0x0312;
    public const int WhMouseLl = 14;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonDown = 0x0204;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    public const uint VkV = 0x56;
    public const uint InputKeyboard = 1;
    public const uint KeyeventfKeyup = 0x0002;
    public const ushort VkControl = 0x11;
    public const ushort VkC = 0x43;

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(
        int hookId, MouseHookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? moduleName);

    public delegate nint MouseHookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    public struct LowLevelMouseInput
    {
        public int X; public int Y; public uint MouseData; public uint Flags;
        public uint Time; public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X; public int Y; public uint MouseData; public uint Flags;
        public uint Time; public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey; public ushort ScanCode; public uint Flags;
        public uint Time; public nint ExtraInfo;
    }

    public static void SendCtrlC()
    {
        var inputs = new[]
        {
            Key(VkControl, false), Key(VkC, false), Key(VkC, true), Key(VkControl, true)
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? KeyeventfKeyup : 0 } }
    };
}
