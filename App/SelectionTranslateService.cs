using System.Windows;
using verba_windows.Services;
using verba_windows.Utilities;

namespace verba_windows.AppHost;

public sealed class SelectionTranslateService : IDisposable
{
    private readonly PanelController _panel;
    private readonly ISelectionProbe _selection;
    private readonly GlobalMouseHook _mouse;
    private readonly TranslateContextWindow _window;
    private CancellationTokenSource? _probeCancellation;
    private string? _selectedText;
    private int _requestId;
    private bool _disposed;

    public SelectionTranslateService(
        PanelController panel,
        ISelectionProbe selection,
        AppLanguageStore language)
    {
        _panel = panel;
        _selection = selection;
        _window = new TranslateContextWindow(language);
        _window.Invoked += OnInvoked;
        _mouse = new GlobalMouseHook();
        _mouse.MouseAction += OnMouseAction;
    }

    private void OnMouseAction(object? sender, GlobalMouseEventArgs e)
    {
        if (_disposed) return;
        if (e.Action == GlobalMouseAction.LeftDown)
        {
            if (!_window.Contains(e.ScreenPoint)) Hide();
            return;
        }
        if (e.Action == GlobalMouseAction.RightDown)
        {
            Hide();
            return;
        }
        if (e.Action != GlobalMouseAction.LeftUp || _window.Contains(e.ScreenPoint)) return;

        var target = NativeMethods.GetForegroundWindow();
        Hide();
        if (target == 0 || IsCurrentProcess(target)) return;
        BeginProbe(e.ScreenPoint, target);
    }

    private void BeginProbe(System.Drawing.Point point, nint target)
    {
        var requestId = ++_requestId;
        _probeCancellation = new CancellationTokenSource();
        _ = ProbeAndShowAsync(point, target, requestId, _probeCancellation.Token);
    }

    private async Task ProbeAndShowAsync(
        System.Drawing.Point point,
        nint target,
        int requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Let the source application finish committing its mouse selection.
            await Task.Delay(150, cancellationToken);
            var selected = await _selection.CaptureWithAutomationAsync(point, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || requestId != _requestId ||
                string.IsNullOrWhiteSpace(selected)) return;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || requestId != _requestId) return;
                _selectedText = selected;
                _window.ShowAt(point, target);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Selection popup probe failed: {ex}");
        }
    }

    private void OnInvoked(object? sender, EventArgs e)
    {
        var selected = _selectedText;
        Hide();
        if (!string.IsNullOrWhiteSpace(selected)) _panel.ShowSelection(selected);
    }

    private void Hide()
    {
        _probeCancellation?.Cancel();
        _probeCancellation?.Dispose();
        _probeCancellation = null;
        _selectedText = null;
        if (_window.IsVisible) _window.Hide();
    }

    private static bool IsCurrentProcess(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
        _mouse.MouseAction -= OnMouseAction;
        _mouse.Dispose();
        _window.Invoked -= OnInvoked;
        _window.Dispose();
    }
}
