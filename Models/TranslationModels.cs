using System.Text.Json.Serialization;

namespace verba_windows.Models;

public sealed class TranslateRequest
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; init; } = "";
    [JsonPropertyName("sourceText")] public string SourceText { get; init; } = "";
    [JsonPropertyName("sourceLang")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLang { get; init; }
    [JsonPropertyName("targetLang")] public string TargetLang { get; init; } = "";
    [JsonPropertyName("tone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tone { get; init; }
    [JsonPropertyName("history")] public IReadOnlyList<HistoryEntry> History { get; init; } = [];
    [JsonPropertyName("refinementInstruction")] public string? RefinementInstruction { get; init; }
}

public sealed record HistoryEntry(
    [property: JsonPropertyName("instruction")] string Instruction,
    [property: JsonPropertyName("resultText")] string ResultText);

public sealed class TranslateResponse
{
    [JsonPropertyName("translation")] public string? Translation { get; init; }
    [JsonPropertyName("cached")] public bool Cached { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
}

public sealed class TranslateErrorResponse
{
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("retryAfterSeconds")] public int? RetryAfterSeconds { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public abstract record TranslationFailure
{
    public sealed record SameLanguages : TranslationFailure;
    public sealed record InvalidResponse : TranslationFailure;
    public sealed record UnexpectedStatus(int Code) : TranslationFailure;
    public sealed record RateLimited(string Message, int? RetryAfterSeconds) : TranslationFailure;
    public sealed record Server(string Message) : TranslationFailure;
    public sealed record Transport(string Message) : TranslationFailure;
}

public sealed class TranslationServiceException(TranslationFailure failure) : Exception
{
    public TranslationFailure Failure { get; } = failure;
}

public enum Tone { Casual, Neutral, Formal }
public static class ToneExtensions
{
    public static string ToApiValue(this Tone tone) => tone.ToString().ToLowerInvariant();
}

public enum RefineAction { Shorter, Natural, KeepTerms, Explain }
public static class RefineActionExtensions
{
    public static string Instruction(this RefineAction action) => action switch
    {
        RefineAction.Shorter => "shorter",
        RefineAction.Natural => "more natural",
        RefineAction.KeepTerms => "keep the technical terms",
        RefineAction.Explain => "explain further",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
