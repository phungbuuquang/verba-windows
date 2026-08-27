using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using verba_windows.Models;

namespace verba_windows.Services;

/// <summary>
/// Reports this installation to the backend once per app launch. Registration is telemetry:
/// it never surfaces an error to the user and never blocks startup.
/// </summary>
public sealed class InstallationApiService : IInstallationApiService
{
    private static readonly Uri Endpoint = new("https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/register-installation");
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions LogJsonOptions = new() { WriteIndented = true };

    private readonly HttpClient _client;

    public InstallationApiService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task RegisterAsync(RegisterInstallationRequest request, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var json = JsonSerializer.Serialize(request, JsonOptions);
        LogApi($"[{requestId}] POST {Endpoint}\n{JsonSerializer.Serialize(request, LogJsonOptions)}");
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var body = Encoding.UTF8.GetString(bytes);
            LogApi($"[{requestId}] {(int)response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms, {bytes.Length} bytes\n{body}");
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogApi($"[{requestId}] transport error: {ex}");
            throw;
        }
    }

    [Conditional("DEBUG")]
    private static void LogApi(string message)
    {
        var line = $"[Installation] {message}";
        Trace.WriteLine(line);
        Console.WriteLine(line);
    }
}
