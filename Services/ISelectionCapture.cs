namespace verba_windows.Services;

public interface ISelectionCapture
{
    Task<string?> CaptureAsync(nint foregroundWindow, CancellationToken cancellationToken = default);
}
