using System.Text.Json;
using System.Windows.Input;
using verba_windows.AppHost;
using verba_windows.Models;
using verba_windows.Services;
using verba_windows.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("JSON omits sourceLang and tone but keeps null refinementInstruction", JsonContract),
    ("HTTP 200 error envelopes are classified before success", ErrorEnvelope),
    ("custom tone is model-facing refinement text", CustomToneInstruction),
    ("new source cancels an in-flight request without an error", Cancellation),
    ("undo/redo cursor and redo truncation", History),
    ("custom tone store deduplicates and caps MRU", ToneStore),
    ("source and result speech use their own text and language", SpeechSides),
    ("shortcut parser and settings persistence", ShortcutSettings),
    ("language catalog fetches, caches, and expires after 24 hours", LanguageCatalogCache),
    ("first launch follows Windows language and saves user choices", LanguageDefaults)
};

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
return failures;

static Task JsonContract()
{
    var request = new TranslateRequest { DeviceId="d", SourceText="x", SourceLang=null, TargetLang="en", Tone=null, History=[], RefinementInstruction=null };
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
    Check(!json.RootElement.TryGetProperty("sourceLang", out _), "sourceLang was serialized");
    Check(!json.RootElement.TryGetProperty("tone", out _), "tone was serialized");
    Check(json.RootElement.GetProperty("refinementInstruction").ValueKind == JsonValueKind.Null, "refinementInstruction null missing");
    return Task.CompletedTask;
}

static async Task ErrorEnvelope()
{
    var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
    { Content = new System.Net.Http.StringContent("{\"error\":\"slow down\",\"retryAfterSeconds\":30}") };
    var service = new TranslationApiService(new System.Net.Http.HttpClient(new FakeHandler(response)));
    try
    {
        await service.TranslateAsync(new TranslateRequest { DeviceId="d", SourceText="x", TargetLang="en" }, default);
        throw new InvalidOperationException("HTTP 200 error was treated as success");
    }
    catch (TranslationServiceException ex)
    {
        Check(ex.Failure is TranslationFailure.RateLimited { Message: "slow down", RetryAfterSeconds: 30 }, "error envelope classification is wrong");
    }
}

static async Task CustomToneInstruction()
{
    var harness = Harness.Create(); harness.Vm.SourceText = "xin chào";
    harness.Vm.SaveTone(null, "warm and concise");
    await Wait(harness.Vm);
    var request = harness.Api.Requests.Single();
    Check(request.Tone is null, "custom tone leaked into tone");
    Check(request.RefinementInstruction == "use this tone: warm and concise", "custom tone instruction is wrong");
    harness.Dispose();
}

static async Task Cancellation()
{
    var api = new FakeApi { BlockFirst = true };
    var harness = Harness.Create(api); harness.Vm.SourceText = "first"; harness.Vm.TranslateNow();
    await Task.Delay(30); harness.Vm.SourceText = "second"; harness.Vm.TranslateNow(); await Wait(harness.Vm);
    Check(harness.Vm.TranslatedText == "result-2", "new request did not win");
    Check(harness.Vm.Failure is null, "cancellation became a visible failure"); harness.Dispose();
}

static async Task History()
{
    var harness = Harness.Create(); harness.Vm.SourceText = "hello"; harness.Vm.TranslateNow(); await Wait(harness.Vm);
    harness.Vm.ToggleAction(RefineAction.Shorter); await Wait(harness.Vm);
    Check(harness.Vm.CanUndo, "undo unavailable"); harness.Vm.Undo();
    Check(harness.Vm.TranslatedText == "result-1" && harness.Vm.CanRedo, "undo cursor wrong");
    harness.Vm.ToggleAction(RefineAction.Natural); await Wait(harness.Vm);
    Check(!harness.Vm.CanRedo && harness.Vm.TranslatedText == "result-3", "redo branch was not truncated"); harness.Dispose();
}

static Task ToneStore()
{
    var path = Path.Combine(Path.GetTempPath(), $"verba-test-{Guid.NewGuid():N}.json");
    var settings = new SettingsStore(path); var store = new CustomToneStore(settings);
    var first = store.Add("Friendly"); var duplicate = store.Add("friendly");
    Check(first.Id == duplicate.Id && store.Tones.Count == 1, "case-insensitive duplicate created");
    for (var i=0; i<13; i++) store.Add("tone " + i);
    Check(store.Tones.Count == 12 && store.Tones[0].Instruction == "tone 12", "MRU/cap incorrect");
    try { File.Delete(path); } catch { }
    return Task.CompletedTask;
}

static async Task SpeechSides()
{
    var harness = Harness.Create();
    harness.Vm.SourceText = "xin chào";
    Check(harness.Vm.CanSpeakSource, "source speech should be available");

    harness.Vm.ToggleSourceSpeech();
    Check(harness.Vm.IsSpeakingSource && !harness.Vm.IsSpeakingResult, "source speaking state is wrong");
    Check(harness.Speech.LastText == "xin chào" && harness.Speech.LastLanguageId == "vi", "source speech payload is wrong");

    harness.Vm.TranslateNow();
    await Wait(harness.Vm);
    Check(!harness.Vm.IsSpeaking, "translation did not stop source speech");

    harness.Vm.ToggleResultSpeech();
    Check(!harness.Vm.IsSpeakingSource && harness.Vm.IsSpeakingResult, "result speaking state is wrong");
    Check(harness.Speech.LastText == "result-1" && harness.Speech.LastLanguageId == "en", "result speech payload is wrong");

    harness.Vm.ToggleSourceSpeech();
    Check(harness.Vm.IsSpeakingSource && !harness.Vm.IsSpeakingResult, "switching speech sides left the wrong side active");
    harness.Dispose();
}

static Task ShortcutSettings()
{
    var path = Path.Combine(Path.GetTempPath(), $"verba-test-{Guid.NewGuid():N}.json");
    try
    {
        var shortcut = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.K);
        Check(shortcut.IsValid && shortcut.DisplayText == "Ctrl+Alt+K", "shortcut display text is wrong");
        Check(HotkeyGesture.TryParse(shortcut.DisplayText, out var parsed) && parsed == shortcut, "shortcut did not round-trip");
        Check(!HotkeyGesture.TryParse("K", out _), "shortcut without a modifier was accepted");

        var settings = new SettingsStore(path);
        settings.SetShortcut(shortcut.DisplayText);
        Check(new SettingsStore(path).Shortcut == "Ctrl+Alt+K", "shortcut was not persisted");
    }
    finally { try { File.Delete(path); } catch { } }
    return Task.CompletedTask;
}

static async Task LanguageCatalogCache()
{
    var path = Path.Combine(Path.GetTempPath(), $"verba-languages-{Guid.NewGuid():N}.json");
    const string json = """
        {"languages":[
          {"code":"vi","name":"Vietnamese","nativeName":"Tiếng Việt","flag":"🇻🇳","countryCode":"VN"},
          {"code":"en","name":"English","nativeName":"English","flag":"🇬🇧","countryCode":"GB"}
        ],"version":"b22b22d9ab2fb651"}
        """;
    var fetchedAt = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    try
    {
        var firstHandler = new LanguageHandler(json);
        var first = new TranslationLanguageCatalog(new System.Net.Http.HttpClient(firstHandler), path);
        Check(await first.RefreshIfStaleAsync(fetchedAt), "empty cache did not fetch");
        Check(firstHandler.RequestCount == 1, "language endpoint was not called once");
        Check(first.Version == "b22b22d9ab2fb651", "language version was not retained");
        Check(first.Languages.Count == 2 && first.Languages[0] == new TranslationLanguage("vi", "Vietnamese", "Tiếng Việt", "🇻🇳", "VN"),
            "language payload was not mapped");

        var secondHandler = new LanguageHandler(json);
        var cached = new TranslationLanguageCatalog(new System.Net.Http.HttpClient(secondHandler), path);
        Check(cached.Languages.Count == 2, "cached languages were not loaded at startup");
        Check(!await cached.RefreshIfStaleAsync(fetchedAt.AddHours(23).AddMinutes(59)), "fresh cache was refreshed early");
        Check(secondHandler.RequestCount == 0, "fresh cache called the endpoint");
        Check(await cached.RefreshIfStaleAsync(fetchedAt.AddHours(24)), "24-hour cache was not refreshed");
        Check(secondHandler.RequestCount == 1, "expired cache did not call the endpoint");
    }
    finally
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".tmp"); } catch { }
    }
}

static Task LanguageDefaults()
{
    var path = Path.Combine(Path.GetTempPath(), $"verba-language-defaults-{Guid.NewGuid():N}.json");
    var cachePath = path + ".languages";
    try
    {
        var settings = new SettingsStore(path);
        var speech = new FakeSpeech();
        using var vm = new TranslationViewModel(new FakeApi(), speech, settings,
            new AppLanguageStore(settings), new CustomToneStore(settings),
            new TranslationLanguageCatalog(cachePath: cachePath), "vi-VN");

        Check(vm.IsAutoDetectSource, "source auto-detection was not enabled on first launch");
        Check(vm.TargetLanguage.Id == "vi", "Windows UI language was not selected as the target");
        Check(vm.TargetLanguage.Flag == "🇻🇳", "fallback language flag is missing");
        Check(vm.SourceLanguage.Id == "en", "manual source fallback should differ from the target");

        vm.SetAutoDetectSource(false);
        vm.SetSourceLanguage(vm.Languages.Single(x => x.Id == "ja"));
        vm.SetTargetLanguage(vm.Languages.Single(x => x.Id == "fr"));
        var saved = new SettingsStore(path);
        Check(saved.AutoDetectSource == false && saved.SourceLanguage == "ja" && saved.TargetLanguage == "fr",
            "user language choices were not persisted");
    }
    finally
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(cachePath); } catch { }
    }
    return Task.CompletedTask;
}

static async Task Wait(TranslationViewModel vm)
{
    var until = DateTime.UtcNow.AddSeconds(2);
    while (vm.IsTranslating && DateTime.UtcNow < until) await Task.Delay(10);
    Check(!vm.IsTranslating, "translation timed out");
}

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeApi : ITranslationApiService
{
    public List<TranslateRequest> Requests { get; } = [];
    public bool BlockFirst { get; init; }
    public async Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request); var number = Requests.Count;
        if (BlockFirst && number == 1) await Task.Delay(Timeout.Infinite, cancellationToken);
        await Task.Delay(5, cancellationToken);
        return new TranslateResponse { Translation = $"result-{number}" };
    }
}

sealed class FakeHandler(System.Net.Http.HttpResponseMessage response) : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
}

sealed class LanguageHandler(string json) : System.Net.Http.HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
        System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (request.Method != System.Net.Http.HttpMethod.Get ||
            request.RequestUri?.AbsoluteUri != "https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/languages")
            throw new InvalidOperationException("language endpoint request is wrong");
        return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

sealed class FakeSpeech : ISpeechService
{
    public event EventHandler<bool>? SpeakingChanged;
    public string? LastText { get; private set; }
    public string? LastLanguageId { get; private set; }
    private bool IsSpeaking { get; set; }
    public bool HasMatchingVoice(string languageId) => true;
    public void Speak(string text, string languageId)
    {
        if (IsSpeaking) SpeakingChanged?.Invoke(this, false);
        LastText = text; LastLanguageId = languageId; IsSpeaking = true;
        SpeakingChanged?.Invoke(this, true);
    }
    public void Stop() { IsSpeaking = false; SpeakingChanged?.Invoke(this, false); }
    public void Dispose() { }
}

sealed class Harness : IDisposable
{
    private Harness(TranslationViewModel vm, FakeApi api, FakeSpeech speech, string path) { Vm=vm; Api=api; Speech=speech; Path=path; }
    public TranslationViewModel Vm { get; } public FakeApi Api { get; } public FakeSpeech Speech { get; } private string Path { get; }
    public static Harness Create(FakeApi? api=null)
    {
        api ??= new FakeApi(); var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"verba-test-{Guid.NewGuid():N}.json");
        var speech = new FakeSpeech();
        var settings = new SettingsStore(path);
        settings.SetTranslationPreferences("vi", "en", false);
        var languageCachePath = path + ".languages";
        var vm = new TranslationViewModel(api, speech, settings, new AppLanguageStore(settings),
            new CustomToneStore(settings), new TranslationLanguageCatalog(cachePath: languageCachePath));
        return new Harness(vm, api, speech, path);
    }
    public void Dispose()
    {
        Vm.Dispose();
        try { File.Delete(Path); } catch { }
        try { File.Delete(Path + ".languages"); } catch { }
    }
}
