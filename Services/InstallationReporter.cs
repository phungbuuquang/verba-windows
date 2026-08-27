using verba_windows.Models;
using verba_windows.Utilities;

namespace verba_windows.Services;

/// <summary>
/// Registers the installation once per app launch. This is deliberately not tied to opening the
/// translation panel: it runs from startup so an install is counted even if the panel is never used.
/// </summary>
public sealed class InstallationReporter(
    IInstallationApiService api,
    SettingsStore settings,
    string distributionChannel)
{
    private int _reported;

    /// <summary>
    /// How this copy was delivered. The backend currently whitelists only "mac_app_store" and
    /// "direct", and every Windows build ships outside a store, so both the Velopack-installed and
    /// the portable copy report "direct".
    /// </summary>
    public const string DirectChannel = "direct";

    public RegisterInstallationRequest BuildRequest() => new()
    {
        InstallationId = settings.GetOrCreateDeviceId(),
        Platform = AppEnvironment.Platform,
        DistributionChannel = distributionChannel,
        AppVersion = AppEnvironment.AppVersion,
        BuildNumber = AppEnvironment.BuildNumber,
        OsVersion = AppEnvironment.OsVersion,
        Architecture = AppEnvironment.Architecture
    };

    /// <summary>Fires the registration for this launch. Subsequent calls in the same process are ignored.</summary>
    public async Task ReportLaunchAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 1) return;
        try
        {
            await api.RegisterAsync(BuildRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            // Telemetry must never affect the user's session; InstallationApiService already logged it.
        }
    }
}
