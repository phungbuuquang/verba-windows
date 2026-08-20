using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using verba_windows.Models;

namespace verba_windows.Services;

public sealed class TranslationLanguageCatalog
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private static readonly Uri Endpoint = new("https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/languages");
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly HttpClient _client;
    private readonly string _cachePath;
    private DateTimeOffset? _fetchedAtUtc;
    private IReadOnlyList<TranslationLanguage> _languages = TranslationLanguage.All;

    public TranslationLanguageCatalog(HttpClient? client = null, string? cachePath = null)
    {
        _client = client ?? SharedClient;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "verba", "languages-cache.json");
        LoadCache();
    }

    public IReadOnlyList<TranslationLanguage> Languages => _languages;
    public string? Version { get; private set; }
    public event EventHandler? LanguagesChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var delay = _fetchedAtUtc is null ? TimeSpan.Zero : _fetchedAtUtc.Value + RefreshInterval - now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Trace.WriteLine($"Language list refresh failed: {ex}");
                await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);
            }
        }
    }

    public async Task<bool> RefreshIfStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (_fetchedAtUtc is not null && now - _fetchedAtUtc.Value < RefreshInterval) return false;
        await RefreshAsync(cancellationToken, now);
        return true;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, DateTimeOffset? fetchedAt = null)
    {
        using var response = await _client.GetAsync(Endpoint, cancellationToken).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        var payload = await JsonSerializer.DeserializeAsync<LanguageResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(true);
        var languages = Normalize(payload?.Languages);
        if (languages.Count == 0) throw new InvalidDataException("The language endpoint returned no valid languages.");

        _languages = languages;
        Version = payload?.Version;
        _fetchedAtUtc = fetchedAt ?? DateTimeOffset.UtcNow;
        SaveCache();
        LanguagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var cache = JsonSerializer.Deserialize<LanguageCache>(File.ReadAllText(_cachePath), JsonOptions);
            var languages = Normalize(cache?.Languages);
            if (languages.Count == 0 || cache?.FetchedAtUtc is null) return;
            _languages = languages;
            _fetchedAtUtc = cache.FetchedAtUtc;
            Version = cache.Version;
        }
        catch (Exception ex) { Trace.WriteLine($"Language cache load failed: {ex}"); }
    }

    private void SaveCache()
    {
        try
        {
            var directory = Path.GetDirectoryName(_cachePath)!;
            Directory.CreateDirectory(directory);
            var temporary = _cachePath + ".tmp";
            var payload = new LanguageCache
            {
                Languages = _languages.Select(x => new LanguageItem
                {
                    Code = x.Id, Name = x.EnglishName, NativeName = x.NativeName,
                    Flag = x.Flag, CountryCode = x.CountryCode
                }).ToList(),
                Version = Version,
                FetchedAtUtc = _fetchedAtUtc
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporary, _cachePath, true);
        }
        catch (Exception ex) { Trace.WriteLine($"Language cache save failed: {ex}"); }
    }

    private static IReadOnlyList<TranslationLanguage> Normalize(IEnumerable<LanguageItem>? items) =>
        items?
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Code!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(x => new TranslationLanguage(
                x.Code!.Trim(), x.Name!.Trim(), x.NativeName?.Trim() ?? "",
                x.Flag?.Trim() ?? "", x.CountryCode?.Trim() ?? ""))
            .ToList() ?? [];

    private class LanguageResponse
    {
        [JsonPropertyName("languages")]
        public List<LanguageItem>? Languages { get; set; }
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class LanguageCache : LanguageResponse
    {
        [JsonPropertyName("fetchedAtUtc")]
        public DateTimeOffset? FetchedAtUtc { get; set; }
    }

    private class LanguageItem
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("nativeName")]
        public string? NativeName { get; set; }
        [JsonPropertyName("flag")]
        public string? Flag { get; set; }
        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; set; }
    }
}
