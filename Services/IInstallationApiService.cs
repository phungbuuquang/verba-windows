using verba_windows.Models;

namespace verba_windows.Services;

public interface IInstallationApiService
{
    Task RegisterAsync(RegisterInstallationRequest request, CancellationToken cancellationToken);
}
