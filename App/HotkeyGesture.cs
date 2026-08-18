using System.Windows.Input;
using verba_windows.Utilities;

namespace verba_windows.AppHost;

public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static HotkeyGesture Default { get; } = new(ModifierKeys.Control | ModifierKeys.Shift, Key.V);

    public bool IsValid => Modifiers != ModifierKeys.None && KeyInterop.VirtualKeyFromKey(Key) > 0 && Key is not
        (Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
         Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint NativeModifiers
    {
        get
        {
            uint value = NativeMethods.ModNoRepeat;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) value |= NativeMethods.ModAlt;
            if (Modifiers.HasFlag(ModifierKeys.Control)) value |= NativeMethods.ModControl;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) value |= NativeMethods.ModShift;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) value |= NativeMethods.ModWin;
            return value;
        }
    }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(DisplayKey(Key));
            return string.Join("+", parts);
        }
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        var modifiers = ModifierKeys.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (modifier == ModifierKeys.None) return false;
            modifiers |= modifier;
        }

        var keyText = parts[^1];
        if (keyText.Length == 1 && char.IsDigit(keyText[0])) keyText = "D" + keyText;
        if (!Enum.TryParse<Key>(keyText, true, out var key)) return false;
        var candidate = new HotkeyGesture(modifiers, key);
        if (!candidate.IsValid) return false;
        gesture = candidate;
        return true;
    }

    private static string DisplayKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        return key.ToString();
    }
}

public sealed class ShortcutEventArgs(HotkeyGesture shortcut) : EventArgs
{
    public HotkeyGesture Shortcut { get; } = shortcut;
}
