using System.Windows;
using System.Windows.Automation;
using verba_windows.Utilities;

namespace verba_windows.Services;

public sealed class Win32SelectionCapture : ISelectionCapture
{
    public async Task<string?> CaptureAsync(nint foregroundWindow, CancellationToken cancellationToken = default)
    {
        var uiaTask = Task.Run(ReadWithAutomation, cancellationToken);
        var finished = await Task.WhenAny(uiaTask, Task.Delay(250, cancellationToken));
        if (finished == uiaTask)
        {
            var uiaText = await uiaTask;
            if (!string.IsNullOrWhiteSpace(uiaText)) return uiaText.Trim();
        }
        return await ReadWithClipboardAsync(foregroundWindow, cancellationToken);
    }

    private static string? ReadWithAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused?.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) == true)
            {
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges.Length > 0)
                {
                    var text = ranges[0].GetText(-1);
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"UI Automation selection failed: {ex.Message}"); }
        return null;
    }

    private static async Task<string?> ReadWithClipboardAsync(nint foregroundWindow, CancellationToken cancellationToken)
    {
        var backup = await BackupClipboardAsync(cancellationToken);
        if (backup is null) return null;
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (foregroundWindow != 0) NativeMethods.SetForegroundWindow(foregroundWindow);
        await Task.Delay(30, cancellationToken);
        NativeMethods.SendCtrlC();

        var changed = false;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(20, cancellationToken);
            if (NativeMethods.GetClipboardSequenceNumber() != sequence) { changed = true; break; }
        }
        if (!changed) return null;

        string? text = null;
        for (var i = 0; i < 6; i++)
        {
            try { if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText(); break; }
            catch { await Task.Delay(20, cancellationToken); }
        }

        if (backup is not null)
        {
            for (var i = 0; i < 6; i++)
            {
                try { System.Windows.Clipboard.SetDataObject(backup, true); break; }
                catch { await Task.Delay(20, cancellationToken); }
            }
        }
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static async Task<System.Windows.DataObject?> BackupClipboardAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var source = System.Windows.Clipboard.GetDataObject();
                if (source is null) return new System.Windows.DataObject();
                var backup = new System.Windows.DataObject();
                foreach (var format in source.GetFormats(false))
                {
                    try
                    {
                        var data = source.GetData(format, false);
                        if (data is not null) backup.SetData(format, data, false);
                    }
                    catch { }
                }
                return backup;
            }
            catch { await Task.Delay(20, cancellationToken); }
        }
        return null;
    }
}
