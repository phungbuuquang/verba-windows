using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using verba_windows.Models;
using verba_windows.Services;
using verba_windows.Utilities;

namespace verba_windows.ViewModels;

public sealed class TranslationViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationApiService _service;
    private readonly ISpeechService _speech;
    private readonly SettingsStore _settings;
    private readonly CustomToneStore _customTones;
    private readonly TranslationLanguageCatalog _languageCatalog;
    private readonly string _systemLanguageId;
    private bool _hasSourcePreference;
    private bool _hasTargetPreference;
    private CancellationTokenSource? _pending;
    private string? _inFlightSourceText;
    private string _sourceText = "";
    private string _translatedText = "";
    private TranslationLanguage _sourceLanguage = TranslationLanguage.FromId("vi");
    private TranslationLanguage _targetLanguage = TranslationLanguage.FromId("en");
    private bool _isAutoDetectSource;
    private ToneSelection? _tone;
    private string _freeform = "";
    private bool _isTranslating;
    private TranslationFailure? _failure;
    private SpeechSide _activeSpeechSide;
    private SpeechSide _pendingSpeechSide;
    private readonly List<HistoryEntry> _history = [];
    private int _historyIndex = -1;

    public TranslationViewModel(ITranslationApiService service, ISpeechService speech, SettingsStore settings,
        AppLanguageStore languageStore, CustomToneStore customTones, TranslationLanguageCatalog? languageCatalog = null,
        string? systemLanguageId = null)
    {
        _service = service;
        _speech = speech;
        _settings = settings;
        LanguageStore = languageStore;
        _customTones = customTones;
        _languageCatalog = languageCatalog ?? new TranslationLanguageCatalog();
        _systemLanguageId = systemLanguageId ?? CultureInfo.CurrentUICulture.Name;
        InitializeLanguagePreferences();
        CustomTones = customTones.Tones;
        _speech.SpeakingChanged += OnSpeakingChanged;
        LanguageStore.PropertyChanged += OnLanguageChanged;
        _languageCatalog.LanguagesChanged += OnLanguagesChanged;
    }

    public AppLanguageStore LanguageStore { get; }
    public Strings Strings => LanguageStore.Strings;
    public IReadOnlyList<TranslationLanguage> Languages => _languageCatalog.Languages;
    public ObservableCollection<CustomTone> CustomTones { get; }
    public HashSet<RefineAction> Actions { get; } = [];

    public string SourceText { get => _sourceText; set { if (SetProperty(ref _sourceText, value)) RaiseComputed(); } }
    public string TranslatedText { get => _translatedText; private set { if (SetProperty(ref _translatedText, value)) RaiseComputed(); } }
    public TranslationLanguage SourceLanguage { get => _sourceLanguage; private set { if (SetProperty(ref _sourceLanguage, value)) RaiseComputed(); } }
    public TranslationLanguage TargetLanguage { get => _targetLanguage; private set { if (SetProperty(ref _targetLanguage, value)) RaiseComputed(); } }
    public bool IsAutoDetectSource { get => _isAutoDetectSource; private set { if (SetProperty(ref _isAutoDetectSource, value)) RaiseComputed(); } }
    public ToneSelection? Tone { get => _tone; private set { if (SetProperty(ref _tone, value)) RaiseToneProperties(); } }
    public string Freeform { get => _freeform; set { if (SetProperty(ref _freeform, value)) OnPropertyChanged(nameof(CanApplyFreeform)); } }
    public bool IsTranslating { get => _isTranslating; private set { if (SetProperty(ref _isTranslating, value)) RaiseComputed(); } }
    public TranslationFailure? Failure { get => _failure; private set { if (SetProperty(ref _failure, value)) { OnPropertyChanged(nameof(ErrorMessage)); OnPropertyChanged(nameof(HasError)); } } }
    private SpeechSide ActiveSpeechSide
    {
        get => _activeSpeechSide;
        set
        {
            if (!SetProperty(ref _activeSpeechSide, value)) return;
            OnPropertyChanged(nameof(IsSpeaking));
            OnPropertyChanged(nameof(IsSpeakingSource));
            OnPropertyChanged(nameof(IsSpeakingResult));
            OnPropertyChanged(nameof(SourceSpeechIcon));
            OnPropertyChanged(nameof(ResultSpeechIcon));
            OnPropertyChanged(nameof(SourceSpeechTooltip));
            OnPropertyChanged(nameof(ResultSpeechTooltip));
            OnPropertyChanged(nameof(SpeechIcon));
            OnPropertyChanged(nameof(SpeechTooltip));
        }
    }

    public bool IsEmptyState => string.IsNullOrWhiteSpace(SourceText);
    public bool CanSwapLanguages => !IsAutoDetectSource;
    public bool CanSwapNow => CanSwapLanguages && !IsTranslating;
    public bool CanUndo => !IsTranslating && _historyIndex > 0;
    public bool CanRedo => !IsTranslating && _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    public bool CanCopy => !IsTranslating && TranslatedText.Length > 0;
    public bool CanApplyFreeform => !IsTranslating && Freeform.Trim().Length > 0;
    public bool HasError => Failure is not null;
    public bool CanSpeakSource => !IsTranslating && !IsAutoDetectSource && !string.IsNullOrWhiteSpace(SourceText)
        && _speech.HasMatchingVoice(SourceLanguage.Id);
    public bool CanSpeakResult => !IsTranslating && !string.IsNullOrWhiteSpace(TranslatedText)
        && _speech.HasMatchingVoice(TargetLanguage.Id);
    public bool CanSpeak => CanSpeakResult;
    public bool IsSpeaking => ActiveSpeechSide != SpeechSide.None;
    public bool IsSpeakingSource => ActiveSpeechSide == SpeechSide.Source;
    public bool IsSpeakingResult => ActiveSpeechSide == SpeechSide.Result;
    public string SourceSpeechTooltip => IsSpeakingSource ? Strings.StopSpeaking
        : CanSpeakSource ? Strings.SpeakSource : Strings.VoiceUnavailable;
    public string ResultSpeechTooltip => IsSpeakingResult ? Strings.StopSpeaking
        : CanSpeakResult ? Strings.SpeakResult : Strings.VoiceUnavailable;
    public string SourceSpeechIcon => IsSpeakingSource ? "■" : "🔊";
    public string ResultSpeechIcon => IsSpeakingResult ? "■" : "🔊";
    public string SpeechTooltip => ResultSpeechTooltip;
    public string SpeechIcon => ResultSpeechIcon;
    public string AutoDetectTooltip => IsAutoDetectSource ? Strings.AutoDetectOnHelp : Strings.AutoDetectOffHelp;
    public string SwapTooltip => CanSwapLanguages ? Strings.SwapLanguages : Strings.SwapLanguagesDisabled;
    public string SourceLanguageName => SourceLanguage.Name(LanguageStore.Current);
    public string TargetLanguageName => TargetLanguage.Name(LanguageStore.Current);
    public string TrialText => Strings.TrialDaysLeft(5);
    public string ErrorMessage => Failure switch
    {
        TranslationFailure.SameLanguages => Strings.ErrorSameLanguages,
        TranslationFailure.InvalidResponse => Strings.ErrorInvalidResponse,
        TranslationFailure.UnexpectedStatus x => Strings.ErrorServerStatus(x.Code),
        TranslationFailure.RateLimited x => x.RetryAfterSeconds is int s ? $"{x.Message} ({s}s)" : x.Message,
        TranslationFailure.Server x => x.Message,
        TranslationFailure.Transport x => x.Message,
        _ => ""
    };

    public bool IsCasual => Tone == new ToneSelection.Preset(Models.Tone.Casual);
    public bool IsNeutral => Tone == new ToneSelection.Preset(Models.Tone.Neutral);
    public bool IsFormal => Tone == new ToneSelection.Preset(Models.Tone.Formal);
    public bool IsShorter => Actions.Contains(RefineAction.Shorter);
    public bool IsNatural => Actions.Contains(RefineAction.Natural);
    public bool IsKeepTerms => Actions.Contains(RefineAction.KeepTerms);
    public bool IsExplain => Actions.Contains(RefineAction.Explain);

    public void TranslateNow()
    {
        var text = SourceText.Trim();
        if (IsTranslating && text == _inFlightSourceText) return;
        Start(null, true);
    }

    public bool TranslateExternalSelection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        SourceText = text.Trim();
        TranslateNow();
        return true;
    }

    public void ApplyRefinement()
    {
        if (IsTranslating || IsEmptyState) return;
        Start(RefinementInstruction, false);
    }

    public void ApplyFreeform()
    {
        if (IsTranslating) return;
        var instruction = Freeform.Trim();
        if (instruction.Length == 0) return;
        Freeform = "";
        Start(instruction, false);
    }

    public void ClearAll()
    {
        _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        _inFlightSourceText = null; IsTranslating = false;
        StopSpeech(); SourceText = ""; TranslatedText = ""; Freeform = "";
        Actions.Clear(); Tone = null; Failure = null; _history.Clear(); _historyIndex = -1;
        RaiseActionProperties(); RaiseComputed();
    }

    public void SetAutoDetectSource(bool enabled)
    {
        if (IsTranslating || enabled == IsAutoDetectSource) return;
        IsAutoDetectSource = enabled;
        if (!enabled) _hasSourcePreference = true;
        SaveLanguagePreferences();
        if (!IsEmptyState) Start(null, true);
    }

    public void SetSourceLanguage(TranslationLanguage language)
    {
        if (IsTranslating || SourceLanguage == language) return;
        SourceLanguage = language;
        _hasSourcePreference = true;
        SaveLanguagePreferences();
        if (!IsEmptyState) Start(null, true);
    }

    public void SetTargetLanguage(TranslationLanguage language)
    {
        if (IsTranslating || TargetLanguage == language) return;
        TargetLanguage = language;
        _hasTargetPreference = true;
        SaveLanguagePreferences();
        if (!IsEmptyState) Start(null, true);
    }

    public void SwapLanguages()
    {
        if (!CanSwapLanguages || IsTranslating) return;
        (SourceLanguage, TargetLanguage) = (TargetLanguage, SourceLanguage);
        _hasSourcePreference = _hasTargetPreference = true;
        SaveLanguagePreferences();
        if (TranslatedText.Length > 0) { SourceText = TranslatedText; TranslatedText = ""; }
        Start(null, true);
    }

    public void Undo() { if (!CanUndo) return; _historyIndex--; TranslatedText = _history[_historyIndex].ResultText; RaiseComputed(); }
    public void Redo() { if (!CanRedo) return; _historyIndex++; TranslatedText = _history[_historyIndex].ResultText; RaiseComputed(); }

    public void ToggleSourceSpeech() => ToggleSpeech(SpeechSide.Source, SourceText, SourceLanguage.Id, CanSpeakSource);

    public void ToggleResultSpeech() => ToggleSpeech(SpeechSide.Result, TranslatedText, TargetLanguage.Id, CanSpeakResult);

    public void ToggleSpeech() => ToggleResultSpeech();

    public void ToggleTone(Models.Tone value)
    {
        if (IsTranslating) return;
        var selection = new ToneSelection.Preset(value);
        Tone = Tone == selection ? null : selection;
        ApplyRefinement();
    }

    public void ToggleCustomTone(CustomTone value)
    {
        if (IsTranslating) return;
        var selection = new ToneSelection.Custom(value);
        Tone = Tone == selection ? null : selection;
        _customTones.MarkUsed(value);
        ApplyRefinement();
    }

    public void ToggleAction(RefineAction action)
    {
        if (IsTranslating) return;
        if (!Actions.Add(action)) Actions.Remove(action);
        RaiseActionProperties();
        ApplyRefinement();
    }

    public CustomTone SaveTone(CustomTone? existing, string instruction)
    {
        var wasApplied = existing is not null && Tone?.CustomTone?.Id == existing.Id;
        var saved = existing is null ? _customTones.Add(instruction) : _customTones.Update(existing, instruction);
        if (existing is null || wasApplied)
        {
            Tone = new ToneSelection.Custom(saved);
            ApplyRefinement();
        }
        return saved;
    }

    public void DeleteTone(CustomTone tone)
    {
        _customTones.Delete(tone);
        if (Tone?.CustomTone?.Id == tone.Id) Tone = null;
    }

    public void StopSpeech()
    {
        _pendingSpeechSide = SpeechSide.None;
        _speech.Stop();
    }

    private void ToggleSpeech(SpeechSide side, string text, string languageId, bool canSpeak)
    {
        if (ActiveSpeechSide == side)
        {
            StopSpeech();
            return;
        }
        if (!canSpeak) return;
        _pendingSpeechSide = side;
        _speech.Speak(text, languageId);
    }

    private string? RefinementInstruction
    {
        get
        {
            var parts = Enum.GetValues<RefineAction>().Where(Actions.Contains).Select(x => x.Instruction()).ToList();
            if (Freeform.Length > 0) parts.Add(Freeform);
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }

    private string? OutgoingInstruction(string? instruction)
    {
        var parts = new[] { Tone?.Instruction, instruction }.Where(x => !string.IsNullOrEmpty(x)).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private void Start(string? instruction, bool resetHistory)
    {
        StopSpeech(); _pending?.Cancel(); _pending?.Dispose();
        var cts = new CancellationTokenSource(); _pending = cts;
        _ = RunAsync(instruction, resetHistory, cts.Token);
    }

    private async Task RunAsync(string? instruction, bool resetHistory, CancellationToken cancellationToken)
    {
        Failure = null;
        var text = SourceText.Trim();
        if (text.Length == 0) { TranslatedText = ""; return; }
        if (!IsAutoDetectSource && SourceLanguage == TargetLanguage) { Failure = new TranslationFailure.SameLanguages(); return; }
        if (resetHistory) { _history.Clear(); _historyIndex = -1; RaiseComputed(); }
        IsTranslating = true; _inFlightSourceText = text;
        var outgoing = OutgoingInstruction(instruction);
        var request = new TranslateRequest
        {
            DeviceId = _settings.GetOrCreateDeviceId(), SourceText = text,
            SourceLang = IsAutoDetectSource ? null : SourceLanguage.Id, TargetLang = TargetLanguage.Id,
            Tone = Tone?.ApiValue, History = _history.ToList(), RefinementInstruction = outgoing
        };
        try
        {
            var response = await _service.TranslateAsync(request, cancellationToken);
            TranslatedText = response.Translation!;
            PushHistory(outgoing ?? "initial", response.Translation!);
        }
        catch (OperationCanceledException) { return; }
        catch (TranslationServiceException ex) { if (!cancellationToken.IsCancellationRequested) Failure = ex.Failure; }
        catch (Exception ex) { if (!cancellationToken.IsCancellationRequested) Failure = new TranslationFailure.Transport(ex.Message); }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) { IsTranslating = false; _inFlightSourceText = null; }
        }
    }

    private void PushHistory(string instruction, string resultText)
    {
        if (_history.Count > 0 && _history[^1].ResultText == resultText) return;
        if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(new HistoryEntry(instruction, resultText)); _historyIndex = _history.Count - 1; RaiseComputed();
    }

    private void OnSpeakingChanged(object? sender, bool value)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySpeakingState(value);
        else dispatcher.BeginInvoke(() => ApplySpeakingState(value));
    }

    private void ApplySpeakingState(bool isSpeaking)
    {
        if (isSpeaking)
        {
            ActiveSpeechSide = _pendingSpeechSide;
            _pendingSpeechSide = SpeechSide.None;
        }
        else if (_pendingSpeechSide == SpeechSide.None)
        {
            ActiveSpeechSide = SpeechSide.None;
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Strings)); OnPropertyChanged(nameof(ErrorMessage)); OnPropertyChanged(nameof(SpeechTooltip));
        OnPropertyChanged(nameof(SourceSpeechTooltip)); OnPropertyChanged(nameof(ResultSpeechTooltip));
        OnPropertyChanged(nameof(AutoDetectTooltip)); OnPropertyChanged(nameof(SwapTooltip));
        OnPropertyChanged(nameof(SourceLanguageName)); OnPropertyChanged(nameof(TargetLanguageName)); OnPropertyChanged(nameof(TrialText));
    }

    private void OnLanguagesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Languages));
        ApplyLanguageCatalog();
    }

    private void ApplyLanguageCatalog()
    {
        if (_languageCatalog.Languages.Count == 0) return;
        TargetLanguage = _hasTargetPreference
            ? FindLanguage(TargetLanguage.Id) ?? ResolveSystemTarget()
            : ResolveSystemTarget();
        SourceLanguage = _hasSourcePreference
            ? FindLanguage(SourceLanguage.Id) ?? ResolveDefaultSource(TargetLanguage)
            : ResolveDefaultSource(TargetLanguage);
    }

    private void InitializeLanguagePreferences()
    {
        _hasSourcePreference = !string.IsNullOrWhiteSpace(_settings.SourceLanguage);
        _hasTargetPreference = !string.IsNullOrWhiteSpace(_settings.TargetLanguage);
        _isAutoDetectSource = _settings.AutoDetectSource ?? true;
        _targetLanguage = FindLanguage(_settings.TargetLanguage) ?? ResolveSystemTarget();
        _sourceLanguage = FindLanguage(_settings.SourceLanguage) ?? ResolveDefaultSource(_targetLanguage);
    }

    private TranslationLanguage ResolveSystemTarget()
    {
        var exact = FindLanguage(_systemLanguageId);
        if (exact is not null) return exact;

        var parts = _systemLanguageId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var baseId = parts.FirstOrDefault();
        if (parts.Length > 1)
        {
            var country = parts[^1];
            if (baseId?.Equals("zh", StringComparison.OrdinalIgnoreCase) == true)
            {
                var simplified = country.Equals("CN", StringComparison.OrdinalIgnoreCase)
                    || country.Equals("SG", StringComparison.OrdinalIgnoreCase);
                var traditional = country.Equals("TW", StringComparison.OrdinalIgnoreCase)
                    || country.Equals("HK", StringComparison.OrdinalIgnoreCase)
                    || country.Equals("MO", StringComparison.OrdinalIgnoreCase);
                if (simplified || traditional)
                {
                    var chineseMatch = FindLanguage(simplified ? "zh-Hans" : "zh-Hant");
                    if (chineseMatch is not null) return chineseMatch;
                }
            }

            var baseCandidate = FindLanguage(baseId)
                ?? _languageCatalog.Languages.FirstOrDefault(x =>
                    x.Id.StartsWith(baseId + "-", StringComparison.OrdinalIgnoreCase));
            if (baseCandidate is not null) return baseCandidate;

            var countryMatch = _languageCatalog.Languages.FirstOrDefault(x =>
                x.CountryCode.Equals(country, StringComparison.OrdinalIgnoreCase));
            if (countryMatch is not null) return countryMatch;
        }

        var baseMatch = FindLanguage(baseId);
        return baseMatch ?? FindLanguage("en") ?? _languageCatalog.Languages[0];
    }

    private TranslationLanguage ResolveDefaultSource(TranslationLanguage target) =>
        _languageCatalog.Languages.FirstOrDefault(x =>
            x.Id.Equals("en", StringComparison.OrdinalIgnoreCase)
            && !x.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
        ?? _languageCatalog.Languages.FirstOrDefault(x => !x.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
        ?? target;

    private TranslationLanguage? FindLanguage(string? id) => string.IsNullOrWhiteSpace(id) ? null :
        _languageCatalog.Languages.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void SaveLanguagePreferences() => _settings.SetTranslationPreferences(
        _hasSourcePreference ? SourceLanguage.Id : null,
        _hasTargetPreference ? TargetLanguage.Id : null,
        IsAutoDetectSource);

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(IsEmptyState)); OnPropertyChanged(nameof(CanSwapLanguages)); OnPropertyChanged(nameof(CanSwapNow)); OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(AutoDetectTooltip)); OnPropertyChanged(nameof(SwapTooltip));
        OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(CanCopy)); OnPropertyChanged(nameof(CanApplyFreeform));
        OnPropertyChanged(nameof(CanSpeak)); OnPropertyChanged(nameof(CanSpeakSource)); OnPropertyChanged(nameof(CanSpeakResult));
        OnPropertyChanged(nameof(SourceSpeechTooltip)); OnPropertyChanged(nameof(ResultSpeechTooltip));
    }
    private void RaiseToneProperties()
    {
        OnPropertyChanged(nameof(IsCasual)); OnPropertyChanged(nameof(IsNeutral)); OnPropertyChanged(nameof(IsFormal)); OnPropertyChanged(nameof(Tone));
    }
    private void RaiseActionProperties()
    {
        OnPropertyChanged(nameof(IsShorter)); OnPropertyChanged(nameof(IsNatural)); OnPropertyChanged(nameof(IsKeepTerms)); OnPropertyChanged(nameof(IsExplain));
    }

    public void Dispose()
    {
        _pending?.Cancel(); _pending?.Dispose(); _speech.SpeakingChanged -= OnSpeakingChanged;
        LanguageStore.PropertyChanged -= OnLanguageChanged;
        _languageCatalog.LanguagesChanged -= OnLanguagesChanged;
        _speech.Dispose();
    }

    private enum SpeechSide { None, Source, Result }
}
