using Velopack;
using Velopack.Sources;

namespace verba_windows.Services;

public sealed class AppUpdateService
{
    private const string RepositoryUrl = "https://github.com/phungbuuquang/verba-windows";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));
    private VelopackAsset? _downloadedUpdate;

    public bool IsInstalled => _manager.IsInstalled;

    public Task<UpdateInfo?> CheckForUpdatesAsync() => _manager.CheckForUpdatesAsync();

    public async Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        _downloadedUpdate = update.TargetFullRelease;
    }

    public bool BeginApplyAndRestart()
    {
        if (_downloadedUpdate is null) return false;
        _manager.WaitExitThenApplyUpdates(_downloadedUpdate, silent: true, restart: true);
        return true;
    }
}
