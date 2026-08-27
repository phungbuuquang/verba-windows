using System.Text.Json.Serialization;

namespace verba_windows.Models;

public sealed class RegisterInstallationRequest
{
    [JsonPropertyName("installationId")] public string InstallationId { get; init; } = "";
    [JsonPropertyName("platform")] public string Platform { get; init; } = "windows";
    [JsonPropertyName("distributionChannel")] public string DistributionChannel { get; init; } = "";
    [JsonPropertyName("appVersion")] public string AppVersion { get; init; } = "";
    [JsonPropertyName("buildNumber")] public string BuildNumber { get; init; } = "";
    [JsonPropertyName("osVersion")] public string OsVersion { get; init; } = "";
    [JsonPropertyName("architecture")] public string Architecture { get; init; } = "";
}
