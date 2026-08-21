namespace verba_windows.Services;

public interface ISelectionCapture
{
    Task<string?> CaptureAsync(nint foregroundWindow, CancellationToken cancellationToken = default);
}

public interface ISelectionProbe
{
    Task<string?> CaptureWithAutomationAsync(
        System.Drawing.Point screenPoint,
        CancellationToken cancellationToken = default);
}
