using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using verba_windows.Models;

namespace verba_windows.Services;

public sealed class TranslationApiService : ITranslationApiService
{
    private static readonly Uri Endpoint = new("https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/translate");
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions LogJsonOptions = new() { WriteIndented = true };

    public TranslationApiService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken)
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

            TranslateErrorResponse? envelope = null;
            try { envelope = JsonSerializer.Deserialize<TranslateErrorResponse>(body, JsonOptions); } catch (JsonException) { }
            if (!string.IsNullOrEmpty(envelope?.Error))
            {
                var failure = response.StatusCode == HttpStatusCode.TooManyRequests || envelope.RetryAfterSeconds is not null
                    ? (TranslationFailure)new TranslationFailure.RateLimited(envelope.Error, envelope.RetryAfterSeconds)
                    : new TranslationFailure.Server(envelope.Error);
                throw new TranslationServiceException(failure);
            }

            if (!response.IsSuccessStatusCode)
                throw new TranslationServiceException(new TranslationFailure.UnexpectedStatus((int)response.StatusCode));

            TranslateResponse? result;
            try { result = JsonSerializer.Deserialize<TranslateResponse>(body, JsonOptions); }
            catch (JsonException) { throw new TranslationServiceException(new TranslationFailure.InvalidResponse()); }
            if (string.IsNullOrEmpty(result?.Translation))
                throw new TranslationServiceException(new TranslationFailure.InvalidResponse());
            LogApi($"[{requestId}] provider={result.Provider}, cached={result.Cached}");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (TranslationServiceException ex) { LogApi($"[{requestId}] {ex.Failure}"); throw; }
        catch (Exception ex)
        {
            LogApi($"[{requestId}] transport error: {ex}");
            throw new TranslationServiceException(new TranslationFailure.Transport(ex.Message));
        }
    }

    [Conditional("DEBUG")]
    private static void LogApi(string message)
    {
        var line = $"[API] {message}";
        Trace.WriteLine(line);
        Console.WriteLine(line);
    }
}
