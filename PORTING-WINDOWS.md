# verba for Windows — Spec for a C# + WPF Port

This document describes the **complete behaviour** of verba (macOS, SwiftUI +
AppKit) so that Codex or another agent can rebuild it as a Windows app in
**C# + WPF (.NET 9)** without reading the Swift source.

Read in order: §1 (what the app is) → §2 (API) → §3 (state machine) → §4 (UI) →
§5 (text capture) → §6 (persistence) → §7 (i18n) → §8 (C# architecture) →
§9 (checklist).

---

## 1. What this app is

A **tray app** (macOS: menu-bar app) with no main window. It does not appear in
the taskbar or Alt-Tab. The user selects text in **any other app** (browser,
Word, VS Code, a PDF…), clicks the tray icon, and a **floating panel** appears
already filled with that text and **translates it immediately**.

Once a translation lands, the user can **refine** it over multiple turns:
- pick a **tone** (Casual / Neutral / Formal, or one they wrote themselves),
- toggle **action chips** (Shorter / More natural / Keep terms / Explain),
- type a **free-form instruction** ("make it more formal", "drop the last
  sentence").

Each refinement is a new request that carries the **conversation history**, so
the server builds on the previous result instead of translating from scratch.
There is undo/redo across results, text-to-speech for the translation, and a
**Copy & close** button (mac ⌘⏎ → Windows **Ctrl+Enter**) as the primary action.

**The most important UX property:** the panel **must not steal focus** from the
app in front. On macOS that is an `NSPanel` with `.nonactivatingPanel`. The
Windows equivalent is `WS_EX_NOACTIVATE` — see §4.1.

---

## 2. API (port this verbatim — do not change it)

All translation — the base translation, tones, and every refinement — goes
through a **single endpoint** (a Supabase Edge Function).

```
POST https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/translate
Content-Type: application/json
```

### 2.1 Request body

```jsonc
{
  "deviceId": "string",             // stable per-install UUID
  "sourceText": "string",           // trimmed
  "sourceLang": "vi",               // OMIT THE KEY ENTIRELY when auto-detect is on
  "targetLang": "en",
  "tone": "casual",                 // OMIT THE KEY ENTIRELY until the user picks a tone
  "history": [                      // prior refinement turns; [] on the first turn
    { "instruction": "initial", "resultText": "..." },
    { "instruction": "shorter", "resultText": "..." }
  ],
  "instruction": "shorter, more natural"   // null on the first translate
}
```

**Mandatory rules — getting these wrong breaks the server-side cache:**

| Field | Rule |
|---|---|
| `sourceLang` | When `IsAutoDetectSource == true`, **omit the key entirely** (not `null`, not `""`). The server infers the language. |
| `tone` | Accepts exactly three values: `"casual" \| "neutral" \| "formal"`. While the user has **not** picked a tone, **omit the key**. The server decides what an untoned translation sounds like; the client must **not** substitute `"neutral"`. |
| `tone` (custom) | A tone the user wrote themselves does **not** go in `tone`. It is prepended to `instruction` as `use this tone: <what the user typed>` — including on the **initial** translate, which is the one case where that key would otherwise be `null`. |
| `instruction` | `null` on the first translate of a new source text (except for the custom-tone case above). |
| `history` | Reset to `[]` whenever the source text changes, languages are swapped, or auto-detect is toggled. |

In C#, "omit the key" means `JsonIgnoreCondition.WhenWritingNull` on the
nullable string properties:

```csharp
public sealed class TranslateRequest
{
    [JsonPropertyName("deviceId")]   public string DeviceId { get; init; } = "";
    [JsonPropertyName("sourceText")] public string SourceText { get; init; } = "";

    [JsonPropertyName("sourceLang")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLang { get; init; }          // null ⇒ auto-detect

    [JsonPropertyName("targetLang")] public string TargetLang { get; init; } = "";

    [JsonPropertyName("tone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tone { get; init; }                // null ⇒ no tone picked

    [JsonPropertyName("history")]
    public IReadOnlyList<HistoryEntry> History { get; init; } = [];

    [JsonPropertyName("instruction")]
    public string? Instruction { get; init; }   // sent explicitly as null
}

public sealed record HistoryEntry(
    [property: JsonPropertyName("instruction")] string Instruction,
    [property: JsonPropertyName("resultText")] string ResultText);
```

Note the asymmetry: `sourceLang` and `tone` **disappear from the JSON** when
null, while `instruction` is **still sent as `null`**. (Swift's
`Encodable` drops nil optionals, so the mac client omits it too; the server
accepts both. Sending `null` keeps it aligned with the spec written in
CLAUDE.md.)

### 2.2 Response — success

```json
{ "translation": "…", "cached": true, "provider": "cache" }
```

`provider` is `"cache"` on a cache hit, otherwise the LLM that served the
request. The client currently does not display either field — it only logs them.

### 2.3 Response — failure

Every failure path returns the same shape, differing only in one extra field:

```json
{ "error": "…", "retryAfterSeconds": 30, "reason": "daily_cap" }
```

| Situation | Distinguishing field |
|---|---|
| Rate limit | `retryAfterSeconds` (int) |
| Circuit breaker (daily spend cap) | `reason` (string) |
| Malformed request | neither |

**Critical:** some failures come back with **HTTP 200**. The processing order
must therefore be:

1. Read the body and **try decoding `TranslateErrorResponse` first** (it has an
   `error` field).
   - Decoded **and** (`StatusCode == 429` **or** `retryAfterSeconds != null`) → *rate limited*.
   - Decoded (anything else) → *server error*; display `error` verbatim.
2. **Only then** check for a status code outside 2xx → *unexpectedStatus(code)*.
3. Finally, decode `TranslateResponse`.

Unlike Swift, `System.Text.Json` will happily deserialize an object that is
missing every field (leaving them `null`). So the check must be explicit:

```csharp
var errorEnvelope = JsonSerializer.Deserialize<TranslateErrorResponse>(json, opts);
if (!string.IsNullOrEmpty(errorEnvelope?.Error)) { /* handle failure */ }
```

### 2.4 Client-side failure classification

A failure is stored as a **case, not a finished sentence** — so that switching
the interface language re-words an error that is already on screen.

```csharp
public abstract record TranslationFailure
{
    public sealed record SameLanguages : TranslationFailure;
    public sealed record InvalidResponse : TranslationFailure;
    public sealed record UnexpectedStatus(int Code) : TranslationFailure;
    public sealed record RateLimited(string Message, int? RetryAfterSeconds) : TranslationFailure;
    public sealed record Server(string Message) : TranslationFailure;
    public sealed record Transport(string Message) : TranslationFailure;
}
```

How each maps to displayed text (`ErrorMessage`):

| Case | Text |
|---|---|
| `SameLanguages` | `Strings.ErrorSameLanguages` (localized) |
| `InvalidResponse` | `Strings.ErrorInvalidResponse` (localized) |
| `UnexpectedStatus(code)` | `Strings.ErrorServerStatus(code)` (localized) |
| `RateLimited(msg, secs)` | `msg` verbatim from the server; if `secs` is present, `$"{msg} ({secs}s)"` |
| `Server(msg)` / `Transport(msg)` | `msg` verbatim |

Server-supplied messages are **always passed through as-is** — never re-worded.

### 2.5 Logging

Each request gets a short `requestId` (first 8 characters of a GUID) so a
request line and its response line can be matched up when several translations
overlap. Log the request (pretty-printed JSON), the response (status + elapsed
ms + byte count + body), and every error. Use `ILogger` or Serilog.

---

## 3. State machine — the core logic

This is the part that is easiest to get wrong. It all lives in a single
`TranslationViewModel` class.

### 3.1 State

```csharp
string SourceText          = "";
string TranslatedText      = "";
TranslationLanguage SourceLanguage = TranslationLanguage.Named("vi");
TranslationLanguage TargetLanguage = TranslationLanguage.Named("en");
bool IsAutoDetectSource    = false;   // read-only from outside; set via SetAutoDetectSource
ToneSelection? Tone        = null;    // null = no tone picked
HashSet<RefineAction> Actions = [];   // cumulative — several can be on at once
string Freeform            = "";
bool IsTranslating         = false;
TranslationFailure? Failure = null;
bool IsSpeaking            = false;

List<HistoryEntry> History = [];
int HistoryIndex           = -1;
```

Plus two fields that do **not** raise `PropertyChanged`:

```csharp
CancellationTokenSource? _pending;   // the in-flight request
string? _inFlightSourceText;         // trimmed source text of the in-flight request
```

### 3.2 Computed properties

```csharp
bool IsEmptyState   => string.IsNullOrWhiteSpace(SourceText);
bool CanSwapLanguages => !IsAutoDetectSource;
bool CanUndo => !IsTranslating && HistoryIndex > 0;
bool CanRedo => !IsTranslating && HistoryIndex >= 0 && HistoryIndex < History.Count - 1;
```

`CanUndo`/`CanRedo` are **deliberately** false while translating: the result
area is showing the loading dots, so stepping through history would change
something the user cannot see and would then be overwritten by the response.

### 3.3 `IsTranslating` — the panel-lock rule

From the moment a request goes out until its result (or error) is on screen, the
panel is **read-only**: every trigger refuses to fire. The point is to stop a
second instruction from superseding the first and leaving the user unsure which
one they are looking at.

**Exception:** the two text fields (**source** and **freeform**) and the custom
tone editor **stay typeable**. Only *sending* is blocked. Writing a refinement
while waiting is valid behaviour.

In the UI, "locked" means `IsEnabled = false` **plus** `Opacity = 0.45`. (On
mac, a disabled flat button renders identically to an enabled one, so the
dimming is manual; do the same in WPF for consistency.)

### 3.4 Triggers

All of them funnel into one `Start(instruction, resetHistory)` method, so a new
request always **replaces** the previous one rather than racing it.

#### `TranslateNow()` — base translate
Fired by: Enter in the source field, a new external selection, a language swap,
toggling auto-detect.

```csharp
var text = SourceText.Trim();
// Mashing Enter on unchanged text is a no-op while its request is in flight.
// A new selection replacing the text still goes through.
if (IsTranslating && text == _inFlightSourceText) return;
Start(instruction: null, resetHistory: true);
```

#### `ApplyRefinement()` — tone or action chip toggled
```csharp
if (IsTranslating || IsEmptyState) return;
Start(instruction: Instruction, resetHistory: false);
```

#### `ApplyFreeform()` — Enter or send button in the freeform field
```csharp
if (IsTranslating) return;
var instruction = Freeform.Trim();
if (instruction.Length == 0) return;
Freeform = "";                       // clear the field IMMEDIATELY, before sending
Start(instruction, resetHistory: false);
```

#### `ClearAll()` — the ✕ button in the source pane
Wipes the panel back to its just-opened state. Cancels any in-flight request so
its response cannot repopulate the cleared result.

```csharp
_pending?.Cancel(); _pending = null;
_inFlightSourceText = null;
IsTranslating = false;
_speech.Stop();
SourceText = ""; TranslatedText = ""; Freeform = "";
Actions.Clear(); Tone = null; Failure = null;
History.Clear(); HistoryIndex = -1;
```

#### `SetAutoDetectSource(bool enabled)`
```csharp
if (IsTranslating || enabled == IsAutoDetectSource) return;
IsAutoDetectSource = enabled;
if (IsEmptyState) return;
Start(null, resetHistory: true);     // bypasses the repeat guard: the language pair changed
```
Note: this must **not** touch `SourceLanguage` — turning auto-detect off has to
restore the language the user last picked by hand.

#### `SwapLanguages()`
```csharp
if (!CanSwapLanguages || IsTranslating) return;
(SourceLanguage, TargetLanguage) = (TargetLanguage, SourceLanguage);
if (TranslatedText.Length > 0) { SourceText = TranslatedText; TranslatedText = ""; }
Start(null, resetHistory: true);     // also bypasses the repeat guard
```

#### `Undo()` / `Redo()`
```csharp
if (!CanUndo) return;  HistoryIndex--;  TranslatedText = History[HistoryIndex].ResultText;
if (!CanRedo) return;  HistoryIndex++;  TranslatedText = History[HistoryIndex].ResultText;
```
These only move a cursor through the list — **no API call**.

#### `ToggleSpeech()`
Speaking → stop. Not speaking → read `TranslatedText` in the target language's
voice.

### 3.5 `Start` and `Run` — the networking core

```csharp
private void Start(string? instruction, bool resetHistory)
{
    _speech.Stop();                       // whatever is being read is about to be replaced
    _pending?.Cancel();
    var cts = new CancellationTokenSource();
    _pending = cts;
    _ = RunAsync(instruction, resetHistory, cts.Token);
}

private async Task RunAsync(string? instruction, bool resetHistory, CancellationToken ct)
{
    Failure = null;

    var text = SourceText.Trim();
    if (text.Length == 0) { TranslatedText = ""; return; }

    // Auto-detect cannot clash with the target here — only the server knows
    // what it resolved the source to.
    if (!IsAutoDetectSource && SourceLanguage == TargetLanguage)
    { Failure = new TranslationFailure.SameLanguages(); return; }

    if (resetHistory) { History.Clear(); HistoryIndex = -1; }

    IsTranslating = true;
    _inFlightSourceText = text;

    var outgoing = OutgoingInstruction(instruction);

    var request = new TranslateRequest {
        DeviceId  = DeviceIdentifier.Current,
        SourceText = text,
        SourceLang = IsAutoDetectSource ? null : SourceLanguage.Id,
        TargetLang = TargetLanguage.Id,
        Tone       = Tone?.ApiValue,
        History    = History.ToList(),
        Instruction = outgoing,
    };

    try {
        var response = await _service.TranslateAsync(request, ct);
        TranslatedText = response.Translation;
        // "initial" stays English — this string is model-facing, never read by the user.
        PushHistory(outgoing ?? "initial", response.Translation);
    }
    catch (OperationCanceledException) {
        return;                            // superseded by a newer request — NOT a user-visible error
    }
    catch (Exception ex) {
        if (ct.IsCancellationRequested) return;
        Failure = MapFailure(ex);
    }
    finally {
        // A cancelled request has already handed these two fields over to the
        // request that replaced it, so only the live one may clear them.
        if (!ct.IsCancellationRequested) { IsTranslating = false; _inFlightSourceText = null; }
    }
}
```

Common porting trap: a superseded request surfaces as
`OperationCanceledException` (mac: `URLError.cancelled`). That is **not
something the user did wrong** — it must never land in the error banner.

### 3.6 Building the instruction strings

```csharp
// Action chips + freeform (used by ApplyRefinement)
private string? Instruction {
    get {
        var parts = Actions.Select(a => a.Instruction)
                           .Concat(Freeform.Length == 0 ? [] : new[]{ Freeform })
                           .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}

// What actually goes on the wire — a custom tone always comes first
private string? OutgoingInstruction(string? instruction) {
    var parts = new[] { Tone?.Instruction, instruction }
                .Where(p => !string.IsNullOrEmpty(p)).ToList();
    return parts.Count == 0 ? null : string.Join(", ", parts);
}
```

The order of `Actions` must be **stable** so the server-side cache key is
stable. C#'s `HashSet<T>` does **not** guarantee ordering across runs — iterate
in enum declaration order instead:

```csharp
Enum.GetValues<RefineAction>().Where(Actions.Contains).Select(a => a.Instruction())
```

### 3.7 `PushHistory`

```csharp
private void PushHistory(string instruction, string resultText)
{
    // Same result as the previous turn ⇒ don't record it (the server returned an identical cache hit).
    if (History.Count > 0 && History[^1].ResultText == resultText) return;
    // Mid-history (just undone) and now doing something new ⇒ drop the redo branch.
    if (HistoryIndex < History.Count - 1)
        History.RemoveRange(HistoryIndex + 1, History.Count - HistoryIndex - 1);
    History.Add(new HistoryEntry(instruction, resultText));
    HistoryIndex = History.Count - 1;
}
```

---

## 4. UI

### 4.1 The panel window

| Property | macOS | Windows / WPF |
|---|---|---|
| Borderless | `.borderless` | `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"` |
| Doesn't steal focus | `.nonactivatingPanel` | `WS_EX_NOACTIVATE` (0x08000000) applied in `SourceInitialized` via `SetWindowLong(GWL_EXSTYLE, …)` |
| Not in taskbar / Alt-Tab | `.accessory` activation policy | `ShowInTaskbar="False"` + `WS_EX_TOOLWINDOW` (0x00000080) |
| Always floating | `.level = .floating` | `Topmost="True"` |
| Drop shadow | `hasShadow` | draw a border + `DropShadowEffect`, or round the corners via `Clip` |
| Draggable by background | `isMovableByWindowBackground` | `MouseLeftButtonDown` → `DragMove()` |
| Esc closes | `cancelOperation` | `KeyDown` handler on `Key.Escape` |
| Click-outside closes | global `NSEvent` monitor | `Deactivated` event, or a `WH_MOUSE_LL` low-level hook |
| Remembers position | `setFrameAutosaveName` | persist `Left`/`Top` in settings |

**The `WS_EX_NOACTIVATE` + keyboard problem:** a window with that flag will not
receive keyboard input the ordinary way. Two routes:

1. **Simple:** drop `WS_EX_NOACTIVATE` and accept that the panel takes focus
   when it opens. Since the text is captured *before* the panel appears (§5),
   losing the other app's focus does not break anything — it just feels slightly
   different from the mac build. **Recommended for the first version.**
2. **Faithful to mac:** keep `WS_EX_NOACTIVATE` and call `SetFocus` /
   `SetForegroundWindow` manually after showing, or use `AttachThreadInput` to
   borrow the input queue. Considerably more work — leave it for a later phase.

**Positioning on open:** drop the panel just below the tray icon, horizontally
centred on it, 6px of clearance. Then **clamp it into the screen's working area**
with an 8px margin. If a saved position falls outside every currently attached
display, discard it and fall back to the default placement — a stale off-screen
frame is the classic cause of "I clicked and nothing happened".

### 4.2 Layout

The panel is a **fixed 640pt wide** (use 640 DIP in WPF), 12pt corner radius,
0.5px `#1A000000` border. Height follows content.

```
┌────────────────────────────────────────────────────────────┐
│ PanelHeader   [✨Auto] [vi ▾] [⇄] [**en** ▾]  5 days…  [⚙] │  ~40
├────────────────────────────────────────────────────────────┤  hairline
│ SourceRow (280 wide)     │ ResultRow (remainder)           │
│ ┌ multi-line TextBox  ✕ │ ┌ 17pt text        [🔊]         │  260 FIXED
│ │ scrolls when it       │ │ scrolls when it overflows      │  (each column
│ └ overflows             │ └ error (red, 12pt) at bottom    │   scrolls itself)
├──────────────────────────┴─────────────────────────────────┤  hairline
│ ToneChipRow    (Casual)(Neutral)(Formal)(custom tones…)(+) │  hidden while
│ [inline tone editor when adding/editing]                   │  IsEmptyState
│ ActionChipRow  [←Shorter][💬Natural][</>Keep][💡Explain]   │
├────────────────────────────────────────────────────────────┤  hairline
│ FreeformRow    ↳ [What should change? Just say it…]   [↑]  │
├────────────────────────────────────────────────────────────┤  hairline
│ PanelFooter    [↶][↷][🕘]                    Ctrl⏎ [Copy]  │
└────────────────────────────────────────────────────────────┘
```

The source/result split is a **fixed 260 high** — it does not size to content.
Reason: translations vary in length, and a panel that resizes would jump around
under the pointer. Each column scrolls internally.

The whole tone/action/freeform block is **hidden** when `IsEmptyState == true`
(there is nothing to refine yet).

### 4.3 `PanelHeader`

Left to right: auto-detect button → source language picker → swap button →
target language picker → (spacer) → trial-days counter → gear button.

- **Auto-detect**: a pill button. On: accent background at 16% + the word
  "Auto"/"Tự động"/"자동" + icon. Off: icon only, 6% grey background. Click →
  `SetAutoDetectSource(!active)`.
- **Source picker**: **hidden entirely** while auto-detect is on (the toggle
  already says what the source is, and dropping it keeps the header from
  crowding).
- **Swap button**: disabled + `Opacity 0.4` when `!CanSwapLanguages`. Different
  tooltip per state.
- **Trial counter**: currently **hardcoded to `trialDaysLeft = 5`** in
  `ContentView` — a placeholder, not wired to any entitlement system. Port it
  as-is.
- **Gear menu**: an "App language" submenu (English / Tiếng Việt / 한국어, with a
  ✓ on the active one) → divider → "Check Accessibility permission" → "Open
  System Settings". On Windows the last two are **meaningless** (see §5.4) —
  drop them, or replace with "Settings" / "Quit".

The whole header (except the gear menu) is locked while `IsTranslating`.

### 4.4 `SourceRow`

- Multi-line `TextBox`, `AcceptsReturn=True`, `TextWrapping=Wrap`, borderless,
  14pt font, `VerticalScrollBarVisibility=Auto`.
- **Enter translates. Shift+Enter inserts a newline.** Handle in
  `PreviewKeyDown`:
  ```csharp
  if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) {
      vm.TranslateNow(); e.Handled = true;
  }
  ```
- The blank space below the text field (min 60px) must **accept clicks** and put
  the caret back — so the pane behaves like the rest of an editor. In WPF: give
  the `TextBox` a large `MinHeight`, or put a transparent `Border`
  (`Background="Transparent"`) underneath with a `MouseDown` handler calling
  `sourceBox.Focus()`.
- The ✕ button (`ClearAll`) appears only when `SourceText` is non-empty; after
  clearing, the caret goes back to the source field.
- **A note from the mac build:** they use a `TextField`, not a `TextEditor`,
  because `TextEditor` broke Vietnamese input (Telex/VNI) — it dropped
  marked-text composition mid-keystroke. WPF's `TextBox` handles IME correctly,
  **but you must still test with a Vietnamese IME** (Unikey or the Windows
  Vietnamese keyboard), especially the interaction between the IME and the
  `PreviewKeyDown` handler that intercepts Enter. Pressing Enter to commit an
  IME candidate must **not** trigger a translation — check for
  `Key.ImeProcessed` / `e.ImeProcessedKey`.

### 4.5 `ResultRow`

- While `IsTranslating`: show **three pulsing dots** (LoadingDots) instead of
  text. Do **not** show the previous result — that stops a refinement from being
  mistaken for a finished translation. Animation: each dot animates
  `Opacity 0.25↔1` and `Scale 0.7↔1`, ease-in-out over 0.5s, repeating forever,
  phase-shifted by `index * 0.18s`. In WPF: a `Storyboard` with increasing
  `BeginTime="0:0:0.18"`, `AutoReverse="True"`, `RepeatBehavior="Forever"`.
- When empty: show `"…"` in the muted colour.
- Result text: **17pt** font, 4pt line spacing, **selectable for copying**
  (a borderless read-only `TextBox`, or `SelectableTextBlock`).
- Speak button: shown only when there is a result and nothing is in flight. The
  icon toggles between speaker and stop based on `IsSpeaking`.
- Error banner: at the bottom, 12pt, red.

### 4.6 `ToneChipRow` — the fiddliest component

A **single-select** chip row: three presets + the user's saved tones + a `+`
button. Use a `WrapPanel` (the mac build hand-rolls a `FlowLayout`; WPF has one
built in).

- Clicking the active chip **deselects** it (`Tone = null`) and still calls
  `ApplyRefinement()` — so the panel can get back to the no-tone state it opens
  in.
- Clicking a custom chip also marks it used (`MarkUsed`, moving it to the front).
- Custom chip: label truncated to 22 characters + "…", with the **full text in
  the tooltip**.
- Right-click a custom chip → context menu: Edit / Delete.

**Inline editor** (not a popup or dialog — on mac a child window's text field is
a reliable way to lose the caret; keep it inline on Windows for consistency):

```csharp
private sealed class ToneEdit {
    public CustomTone? Existing;   // null = writing a new one
    public string Draft = "";
}
```
One object rather than two loose fields, so "is it open" and "what is it
editing" can never drift apart.

- Enter (no Shift) → save. Esc → **closes the editor first**; the panel only
  closes on a second Esc, once there is nothing left to back out of.
- The editor is **not** locked while `IsTranslating` (it stays typeable), but the
  save button is: `canSave = !IsTranslating && Draft.Trim().Length > 0`.
- **Saving a new tone** applies it immediately (`Tone = .Custom(saved)` +
  `ApplyRefinement()`) — writing a tone is itself the request to use it.
- **Editing an existing tone** only re-runs if that tone is **currently
  applied**. Read this flag **before** reassigning the selection:
  ```csharp
  var wasApplied = edit.Existing is not null
                && vm.Tone?.CustomTone?.Id == edit.Existing.Id;
  ```
- **Deleting a tone** only drops the chip; it does **not** re-translate. If the
  deleted tone was selected, set `Tone = null`.
- After the editor closes, the caret goes to `.Source` if `IsEmptyState`,
  otherwise to `.Freeform`.

### 4.7 `ActionChipRow`

Four **cumulative** chips (any number can be on). Each toggle immediately calls
`ApplyRefinement()` with the new combination. Locked while `IsTranslating`.

| Enum | Label EN / VI / KO | `instruction` sent to the API (always English) |
|---|---|---|
| `Shorter` | Shorter / Ngắn hơn / 더 짧게 | `shorter` |
| `Natural` | More natural / Tự nhiên hơn / 더 자연스럽게 | `more natural` |
| `KeepTerms` | Keep terms / Giữ thuật ngữ / 용어 유지 | `keep the technical terms` |
| `Explain` | Explain / Giải thích / 설명 추가 | `explain further` |

### 4.8 `FreeformRow`

A ↳ icon + a `TextBox` (1–3 lines) + an ↑ button. Enter (no Shift) calls
`ApplyFreeform()`. The field **stays typeable** while translating; only the send
button is locked.

### 4.9 `PanelFooter`

- Left: undo and redo buttons (disabled per `CanUndo`/`CanRedo`), plus a clock
  icon (decorative).
- Right: a shortcut hint + the **Copy & close** button (accent background, white
  text, 6pt radius).
  - Shortcut: mac ⌘⏎ → **Windows Ctrl+Enter**. Change the displayed label to
    `Ctrl⏎`.
  - On click: copy `TranslatedText` to the clipboard → label flips to
    "Copied"/"Đã chép" → close the panel → after **1.2s**, restore the label.
  - Disabled + `Opacity 0.5` when `TranslatedText` is empty **or**
    `IsTranslating` (otherwise Ctrl+Enter would copy the previous result while
    the new one is still on its way).
- The footer background differs slightly from the panel body (mac:
  `underPageBackgroundColor`).

### 4.10 Shared visual grammar

| Element | Value |
|---|---|
| `Hairline` | 0.5-high `Rectangle`, `#14000000` (black at 8%) |
| `VHairline` | the same, 0.5 wide |
| `panelMuted` | tertiary label colour — WPF: `#8C8C8C` in both themes |
| Chip, **active** | accent background at 16%, accent text, **no** border |
| Chip, **inactive** | transparent background, 0.5px `#26000000` border, secondary text |
| Chip padding | 11 horizontal, 5 vertical |
| Header pill | 9 horizontal, 5 vertical (4 for pickers); `#0F000000` background, or accent at 16% when active |
| Tone chip shape | capsule (fully rounded) |
| Action chip shape | 6pt radius |
| `.locked` | `IsEnabled=false` + `Opacity=0.45` |

The panel must work in both Windows **light and dark mode** — read
`Windows.UI.ViewManagement.UISettings` or the `AppsUseLightTheme` registry
value, and define every colour as a light/dark pair.

### 4.11 Focus (`PanelField`)

A shared enum: `Source`, `Freeform`, `ToneDraft`. The caret moves **between
rows**, so this must be panel-level shared state rather than private to any one
control.

Rules:
- Panel just opened: wait **~60ms**, then put the caret in `Source` if
  `IsEmptyState`, otherwise in `Freeform`. (The delay is required — the control
  does not exist in the first layout pass. In WPF use
  `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)` or `await Task.Delay(60)`.)
- A new external selection arrives: fill `SourceText` → the caret jumps
  **straight to `Freeform`** (the source pane already shows the whole selection;
  the field the user refines from is where they need to type) → `TranslateNow()`.
- Tone editor opens: caret to `ToneDraft` (also after a ~60ms delay).
- Tone editor closes: caret back to `Source` or `Freeform`, per the open rule.

---

## 5. Capturing the selection from another app

This is where the two platforms differ most.

### 5.1 What the macOS build does

Two routes, tried in order:

1. **Accessibility API (AX)** — read `AXSelectedText` from the focused element.
   Never touches the clipboard. Two extra tricks: set `AXManualAccessibility`
   (Electron) and `AXEnhancedUserInterface` (Chrome/Edge/Brave/Vivaldi/Arc),
   because those apps keep their accessibility tree switched off until a client
   asks for it; web content additionally has to be read via
   `AXSelectedTextMarkerRange`, since `AXSelectedText` only covers `<input>` and
   `<textarea>`.
2. **Synthesized Cmd+C** — for apps that never expose `AXSelectedText` at all
   (Preview's PDF view, terminals, VS Code / Monaco). The procedure: **save the
   current clipboard** → send Cmd+C → **poll** `changeCount` every 20ms, up to
   30 times (600ms) → if the clipboard changed, take the text and **restore the
   saved clipboard**; if it did not change (no selection), **leave the clipboard
   completely alone**.

Route 2 runs **only when the user deliberately opens the panel**, never in a
background loop — otherwise it would clobber the user's clipboard constantly.

`SelectionTracker` runs in the background: it listens for global mouse-up and
key-up (with Shift or Cmd held), debounces **150ms**, then reads **via AX only**.
It also remembers the **last external app** (`lastExternalApp`), because once the
panel is open, verba itself is frontmost.

### 5.2 What the Windows build should do

**Primary route — synthesized Ctrl+C.** On Windows this is the common and most
reliable approach (PowerToys, DeepL and others all do it):

```csharp
// 1. Remember the foreground HWND BEFORE the panel appears
var target = GetForegroundWindow();

// 2. Back up the clipboard (all formats, not just text)
var backup = BackupClipboard();
var seqBefore = GetClipboardSequenceNumber();

// 3. Send Ctrl+C to that window (SendInput, or AttachThreadInput + keybd_event)
SendCtrlC();

// 4. Poll 20ms × 30 = 600ms, waiting for the sequence number to change
for (var i = 0; i < 30; i++) {
    await Task.Delay(20);
    if (GetClipboardSequenceNumber() != seqBefore) { text = GetClipboardText(); break; }
}

// 5. Unchanged ⇒ no selection ⇒ DO NOT touch the clipboard; return null
// 6. Got text ⇒ restore the saved clipboard, then return the text
if (text is not null) RestoreClipboard(backup);
```

`GetClipboardSequenceNumber()` is the exact equivalent of
`NSPasteboard.changeCount`. Open and close the clipboard properly and retry —
the Win32 clipboard is frequently held by another app (`OpenClipboard` fails
with `ERROR_ACCESS_DENIED`).

**Secondary route — UI Automation** (the AX equivalent; never touches the
clipboard):

```csharp
var focused = AutomationElement.FocusedElement;
if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var p)) {
    var ranges = ((TextPattern)p).GetSelection();
    if (ranges.Length > 0) return ranges[0].GetText(-1);
}
```
Works with Win32 edit controls, RichEdit, WinForms/WPF, and partially with
Chrome/Edge (**the browser's accessibility must be enabled** — Chromium on
Windows switches its accessibility tree on when it detects a UIA client, much
like `AXEnhancedUserInterface`). Does not work with many Electron apps or most
PDF viewers.

**Recommendation:** try UIA first (silent, no clipboard involvement), fall back
to Ctrl+C. Same order as the mac build.

⚠️ **UIA must run on a background thread** — calling
`AutomationElement.FocusedElement` on the UI thread can block when the target
app is unresponsive. Wrap it in `Task.Run` with a timeout.

### 5.3 The background tracker

The mac build listens for global events to capture a selection *at the moment it
happens*. The Windows equivalents are `WH_MOUSE_LL` + `WH_KEYBOARD_LL` low-level
hooks, or `SetWinEventHook(EVENT_OBJECT_TEXTSELECTIONCHANGED)`.

**However:** with the Ctrl+C route, a background tracker is *unnecessary and
harmful* (it would trash the clipboard continuously). Proposal for Windows:

- **No** background tracker.
- Capture **exactly once, the moment the user opens the panel** (tray click or
  global hotkey), **before** the panel is shown — while the other app still holds
  the foreground and the selection.
- If a background tracker is wanted later, use UIA only (no clipboard) — the
  same reasoning that keeps the mac build's background loop AX-only.

**Global hotkey:** the mac build only has the tray icon. On Windows, add a
hotkey (`Ctrl+Shift+V`) via `RegisterHotKey` — clicking a tray icon forces
the user to move the mouse away from what they just selected.

### 5.3.1 Selection translation popup

Verba uses a `WH_MOUSE_LL` hook to present a no-activate translation popup after
the user finishes selecting text with the left mouse button:

- On left-button up, debounce for about 150ms so the target application can
  commit its selection, then perform an **UI Automation-only** selection read.
  Never use the clipboard from this background path.
- Show `Translate with Verba` only when a non-empty text selection was found.
  The popup must not take focus from the source app.
- Clicking the popup passes the captured text directly to the panel, focuses
  the refinement field, and starts a base translation.
- Hide the popup on the next external click and before any right-click so it
  never overlaps an application's context menu. Ignore clicks inside Verba
  itself, cancel stale probes, and remove the hook during application shutdown.

Applications that do not expose their selection through UI Automation continue
to use the global shortcut and its clipboard fallback.

### 5.4 Permissions

macOS requires **Accessibility** permission; the app shows a `PermissionBanner`
in place of the entire translation UI until it is granted.

**Windows has no equivalent concept** — SendInput and UIA work immediately. So:
- Drop `PermissionBanner`, `AccessibilityPermission`, and the two related menu
  items entirely.
- The one caveat: if the target app runs **as Administrator** and verba does
  not, UIPI blocks SendInput to it. Handle by detecting that case and telling
  the user, or by adding a `uiAccess` manifest (requires code signing and
  installation under Program Files).

**Note when reading the mac source:** `ContentView.refreshPermission()`
currently **hardcodes `hasPermission = true`** (the real check is commented out),
and the `requestWithPrompt()` call at launch is commented out too. So in the
current build the banner **never appears**. Drop it outright when porting.

---

## 6. Persistence

The mac build uses `UserDefaults`. On Windows, use JSON at
`%APPDATA%\verba\settings.json` (recommended over `Properties.Settings` — much
easier to debug).

| Key | Type | Contents |
|---|---|---|
| `verba.deviceId` | string | UUID generated on first run, **never changes**. Sent with every request; the server scopes trial/usage state to it. |
| `verba.appLanguage` | string | `"en"` / `"vi"` / `"ko"`. Defaults to `"en"`. |
| `verba.sourceLanguage` | string? | Last manually selected source language code. |
| `verba.targetLanguage` | string? | Last manually selected target language code. |
| `verba.autoDetectSource` | bool? | Last source auto-detection choice; defaults to `true` on first launch. |
| `verba.customTones` | JSON array | The user's own saved tones. |
| (new) panel position | 2 numbers | `Left` / `Top`. Discard if it falls outside every display. |

### `CustomTone`

```csharp
public sealed record CustomTone(Guid Id, string Instruction, DateTimeOffset CreatedAt)
{
    // Chip label: truncated to 22 characters
    public string Title => Instruction.Length <= 22
        ? Instruction
        : Instruction[..22].TrimEnd() + "…";
}
```

`CustomToneStore` rules:
- Ordered **most-recently-used first** — that is also the chip display order.
- Capped at **12** tones; the oldest falls off the end when exceeded.
- `Add`: if the instruction matches an existing one **case-insensitively**, do
  **not** create a duplicate — move the existing one to the front and return it
  (preserving its `Id`, so a selected chip stays selected).
- `Update`: edit in place, **keeping the `Id`**.
- `MarkUsed`: move to the front (no-op if already there).
- Corrupt JSON, or a payload written by a different build → **catch and return
  an empty list**; never let it take the app down.

`ToneSelection` is a union:

```csharp
public abstract record ToneSelection {
    public sealed record Preset(Tone Tone) : ToneSelection;
    public sealed record Custom(CustomTone Tone) : ToneSelection;

    // Only presets fill the API's `tone` field
    public string? ApiValue => this is Preset p ? p.Tone.ToApiValue() : null;
    // Only custom tones produce an instruction — the scaffolding is always English
    public string? Instruction => this is Custom c ? $"use this tone: {c.Tone.Instruction}" : null;
    public CustomTone? CustomTone => (this as Custom)?.Tone;
}
```

Selection comparison must be **by value** (record equality) — `Tone == selection`
in the view decides which chip is lit.

---

## 7. Interface localization

The app's **own** language (distinct from the translation pair) has three
options: English, Tiếng Việt, 한국어. Defaults to English. Changed from the gear
menu, applied **immediately with no relaunch**.

The mac build deliberately avoids `.strings` files in favour of a plain Swift
table (`Strings`) — bundle-based lookup would need a relaunch to pick up a new
language. In WPF, `.resx` has the same problem (you have to change
`CurrentUICulture` and then refresh every binding by hand). Two options:

1. **A plain C# table** (closest to the mac build, and simplest): a `Strings`
   class with one property per string, resolved through `Pick(en, vi, ko)`.
   `AppLanguageStore` is a singleton implementing `INotifyPropertyChanged`; views
   bind to `AppLanguageStore.Current.Strings.XXX`, and changing the language
   raises `PropertyChanged(nameof(Strings))`, refreshing every binding.
2. `.resx` + a manual `ResourceManager` + a hand-written `MarkupExtension`. More
   work, no clear benefit.

**Go with option 1.**

### 7.1 Strings that must NOT be translated

The following three groups **stay English** regardless of the interface
language, because they travel to the server and determine the **cache key** —
translating them would give every user their own cache entry:

1. `RefineAction.Instruction` — `shorter`, `more natural`,
   `keep the technical terms`, `explain further`.
2. The literal `"initial"` used in `history`.
3. The `"use this tone: "` scaffolding for custom tones (what the user typed
   inside it stays in whatever language they typed).

### 7.2 Full string table

| Property | EN | VI | KO |
|---|---|---|---|
| `AutoDetect` | Auto | Tự động | 자동 |
| `AutoDetectOnHelp` | Detecting the source language automatically — click to pick one yourself | Đang tự nhận diện ngôn ngữ nguồn — bấm để chọn thủ công | 원본 언어를 자동으로 감지하는 중 — 직접 선택하려면 클릭하세요 |
| `AutoDetectOffHelp` | Detect the source language automatically | Tự nhận diện ngôn ngữ nguồn | 원본 언어 자동 감지 |
| `SwapLanguages` | Swap languages | Đảo chiều ngôn ngữ | 언어 바꾸기 |
| `SwapLanguagesDisabled` | Pick a specific source language to swap | Chọn ngôn ngữ nguồn cụ thể để đảo chiều | 바꾸려면 원본 언어를 직접 선택하세요 |
| `TrialDaysLeft(n)` | {n} days left in trial | Còn {n} ngày dùng thử | 체험판 {n}일 남음 |
| `AppLanguage` | App language | Ngôn ngữ ứng dụng | 앱 언어 |
| `SourcePlaceholder` | Select text in another app, or type here… | Chọn văn bản ở app khác, hoặc gõ vào đây… | 다른 앱에서 텍스트를 선택하거나 여기에 입력하세요… |
| `ClearAll` | Clear everything | Xoá tất cả | 모두 지우기 |
| `Translating` | Translating | Đang dịch | 번역 중 |
| `SpeakResult` | Read the translation aloud | Đọc bản dịch | 번역문 읽어주기 |
| `StopSpeaking` | Stop reading | Dừng đọc | 읽기 중지 |
| `AddCustomTone` | Custom tone | Giọng riêng | 커스텀 말투 |
| `AddCustomToneHelp` | Write your own tone and keep it for next time | Tự viết giọng văn và lưu lại cho lần sau | 직접 쓴 말투를 저장해 다음에도 사용하세요 |
| `CustomTonePlaceholder` | Describe the tone, e.g. like a colleague on chat… | Mô tả giọng văn, ví dụ: như đồng nghiệp nhắn tin… | 말투를 설명하세요, 예: 동료와 채팅하듯… |
| `SaveCustomTone` | Save tone | Lưu giọng | 말투 저장 |
| `CancelCustomTone` | Cancel | Huỷ | 취소 |
| `EditCustomTone` | Edit | Sửa | 수정 |
| `DeleteCustomTone` | Delete | Xoá | 삭제 |
| `FreeformPlaceholder` | What should change? Just say it… | Cần sửa gì? Cứ nói… | 무엇을 고칠까요? 편하게 말씀하세요… |
| `Undo` | Undo | Hoàn tác | 실행 취소 |
| `Redo` | Redo | Làm lại | 다시 실행 |
| `CopyAndClose` | Copy & close | Copy và đóng | 복사 후 닫기 |
| `Copied` | Copied | Đã chép | 복사됨 |
| `ErrorSameLanguages` | The source and target languages are the same. | Ngôn ngữ nguồn và đích đang trùng nhau. | 원본 언어와 번역 언어가 동일합니다. |
| `ErrorInvalidResponse` | The server sent an invalid response. | Phản hồi không hợp lệ từ máy chủ. | 서버 응답이 올바르지 않습니다. |
| `ErrorServerStatus(c)` | The server returned an error ({c}). | Máy chủ trả về lỗi ({c}). | 서버에서 오류를 반환했습니다 ({c}). |

Tone and action chip labels: see §4.7 and the table below.

| Tone | EN | VI | KO | wire value |
|---|---|---|---|---|
| casual | Casual | Thân mật | 친근하게 | `casual` |
| neutral | Neutral | Trung tính | 중립적으로 | `neutral` |
| formal | Formal | Trang trọng | 격식 있게 | `formal` |

The macOS-only strings — `CheckAccessibility`, `OpenSystemSettings`,
`PermissionTitle`, `PermissionBody`, `EditSource` — should be **dropped**.

### 7.3 Translation language list

The translation language picker is populated from
`GET https://dtindqvothjuqpmiytsi.supabase.co/functions/v1/languages`. Load the
cached response at startup, refresh it in the background when it is at least 24
hours old, and continue refreshing every 24 hours while the app remains open.
Network or invalid-response failures must leave the last valid cached list in
place. The built-in list below is the offline fallback before any cache exists.

`ar`, `zh-Hans`, `zh-Hant`, `nl`, `en`, `fr`, `de`, `hi`, `id`, `it`, `ja`,
`ko`, `pl`, `pt-BR`, `ru`, `es`, `th`, `tr`, `uk`, `vi`.

Display names: ask the platform for the name localized into the interface
language, falling back to the English name. In .NET:

```csharp
public string Name(AppLanguage appLang) {
    try {
        var ui = CultureInfo.GetCultureInfo(appLang.ToString());   // en / vi / ko
        return CultureInfo.GetCultureInfo(Id).GetDisplayName(ui) ?? EnglishName;
    } catch { return EnglishName; }
}
```
(.NET uses ICU, so `zh-Hans` and `pt-BR` are both valid. Wrap in try/catch
anyway.)

On first launch, source auto-detection is enabled and the target is matched to
the Windows UI culture (exact tag, then country/base language), falling back to
English. Persist the user's source, target, and auto-detect choices for later
launches. Combo-box selection is keyed by language code so a catalog refresh
does not clear the current choice.

---

## 8. Suggested C# architecture

Keep the mac build's MVVM layering — it is the strongest thing about this
codebase.

```
verba.Windows/
├── App/
│   ├── App.xaml(.cs)             // entry point, no StartupUri
│   ├── TrayIcon.cs               // NotifyIcon + context menu
│   ├── PanelWindow.xaml(.cs)     // the floating window (≈ FloatingPanel)
│   └── PanelController.cs        // show/hide, positioning, click-outside, hotkey
├── Models/
│   ├── AppLanguage.cs
│   ├── TranslationLanguage.cs
│   ├── TranslationModels.cs      // request/response/Tone/RefineAction/Failure
│   └── CustomTone.cs             // + ToneSelection
├── ViewModels/
│   └── TranslationViewModel.cs   // ALL of §3
├── Views/
│   ├── ContentView.xaml          // composition + focus only
│   └── Components/
│       ├── PanelHeader / SourceRow / ResultRow
│       ├── ToneChipRow / ActionChipRow / FreeformRow / PanelFooter
│       ├── LoadingDots.xaml
│       └── PanelStyle.xaml       // ResourceDictionary: chips, pills, hairlines, colours
├── Services/
│   ├── ITranslationApiService.cs + TranslationApiService.cs
│   ├── ISelectionCapture.cs     + Win32SelectionCapture.cs   // §5
│   ├── ISpeechService.cs        + SpeechService.cs           // §8.1
│   ├── AppLanguageStore.cs      // singleton, INotifyPropertyChanged
│   ├── CustomToneStore.cs       // singleton
│   └── SettingsStore.cs         // JSON in %APPDATA%
└── Utilities/
    ├── DeviceIdentifier.cs
    ├── Strings.cs               // the table from §7.2
    └── NativeMethods.cs         // P/Invoke
```

**Layering rules (carried over from the mac build):**
- Views only read state from their view model and call its methods. A view does
  **not** call services, parse JSON, or hold translation/history state.
- View models own state and decisions and call down into services. They hold
  **no** reference to any WPF control.
- Services do I/O and know nothing about the UI.
- Every service gets an interface (`ITranslationApiService`) so the view model
  can be tested against a mock.

### 8.1 Text-to-speech

macOS uses `AVSpeechSynthesizer` (on-device, no network, no API key). The
Windows equivalents are `System.Speech.Synthesis.SpeechSynthesizer` (SAPI, built
in) or `Windows.Media.SpeechSynthesis` (a UWP API with better voices, callable
from WPF via a `Windows.winmd` reference).

Voice selection rules (copy exactly):
1. Script overrides: `zh-Hans` → `zh-CN`, `zh-Hant` → `zh-TW`.
2. Try an exact culture match.
3. Failing that, try the base language (`pt-BR` → `pt`).
4. Failing that, take the first voice whose culture starts with the base
   language.
5. Failing that, use the system default voice (**do not** raise an error).

⚠️ Many Windows machines have **no Vietnamese or Korean voice installed**. If
step 5 is reached, the result gets read in an English voice, which sounds badly
wrong. Consider hiding the speak button when no voice matches the target
language, rather than speaking it incorrectly.

Speaking-state management:
- Utterances **queue up** rather than replacing each other → `Stop()` the current
  one before speaking new text.
- Keep the id of the active utterance. A "finished/cancelled" callback for one
  utterance **can arrive after** the next one has started → compare ids before
  setting `IsSpeaking = false`.
- In `Stop()`: **clear the id first, then call stop** — for the reason above.
- Call `Stop()` in three places: `ClearAll()`, at the top of every `Start()`, and
  when the panel closes.
- .NET's `SpeechSynthesizer` is blocking — use `SpeakAsync`, and marshal
  callbacks back to the UI thread via the `Dispatcher`.

### 8.2 Async

Swift's `@MainActor` means everything in the view model runs on the main thread.
In C#:
- The view model runs on the UI thread; `await` with WPF's default
  `SynchronizationContext` returns to the right thread automatically.
- Inside **services**, use `.ConfigureAwait(false)`.
- Inside the **view model**, do **not** use `ConfigureAwait(false)` (you need the
  UI thread to set properties).
- `Task.Delay` replaces `Task.sleep`; `CancellationTokenSource` replaces
  `Task.cancel()`.

### 8.3 HttpClient

A single instance reused app-wide (`IHttpClientFactory` or a `static readonly`
field). Set a sensible timeout (the mac build uses `URLSession`'s 60s default).
Pass the `CancellationToken` straight through to `SendAsync`.

---

## 9. Porting checklist

### Must be exact (wrong here breaks logic or the server cache)
- [ ] `sourceLang` **disappears from the JSON** under auto-detect — not `null`.
- [ ] `tone` **disappears from the JSON** when no tone is picked; the client never substitutes `"neutral"`.
- [ ] A custom tone goes into `instruction` (`use this tone: …`), **including on the first translate**.
- [ ] Decode the error envelope **before** checking the status code (failures can be HTTP 200).
- [ ] `history` resets on: source text change, language swap, auto-detect toggle. It does **not** reset on refinements.
- [ ] Model-facing strings stay English: action chip instructions, `"initial"`, `"use this tone: "`.
- [ ] Action chip order is stable (iterate in enum order, not `HashSet` order).
- [ ] `deviceId` is generated **once** and never changes.

### Logic
- [ ] `Start()` cancels the previous request before dispatching a new one.
- [ ] A cancelled request produces **no** error banner.
- [ ] `finally` clears `IsTranslating`/`_inFlightSourceText` only when **not** cancelled.
- [ ] Repeated Enter on unchanged text is a no-op while in flight; new text still goes through.
- [ ] `SwapLanguages` and `SetAutoDetectSource` **bypass** the repeat guard.
- [ ] `SetAutoDetectSource` does not touch `SourceLanguage`.
- [ ] `PushHistory` skips a result identical to the previous turn, and truncates the redo branch after an undo.
- [ ] `ClearAll` cancels the in-flight request and stops TTS.

### UI/UX
- [ ] The panel does not appear in the taskbar or Alt-Tab.
- [ ] Esc closes the panel; but with the tone editor open, the first Esc closes the editor.
- [ ] Clicking outside closes the panel.
- [ ] The panel always lands on-screen; a saved off-screen position is discarded.
- [ ] Enter sends, Shift+Enter inserts a newline — in **all three** fields (source, freeform, tone draft).
- [ ] Ctrl+Enter is Copy & close; disabled when empty **or** translating.
- [ ] The tone/action/freeform block is hidden while `IsEmptyState`.
- [ ] The source/result split is a **fixed 260 high**, each column scrolling internally.
- [ ] While translating: loading dots show, and the previous result does **not**.
- [ ] While translating: both text fields stay typeable; only the send affordances lock.
- [ ] Locked means `IsEnabled=false` **plus** `Opacity=0.45`.
- [ ] Clicking the active tone chip deselects it.
- [ ] Saving a new tone applies it immediately; editing a tone re-runs only if that tone is currently applied.
- [ ] Deleting a tone only drops the chip — it does **not** re-translate.
- [ ] Focus moves per §4.11 (remember the ~60ms delay).
- [ ] The "Copied" label reverts after 1.2s.
- [ ] Works in both light and dark mode.

### Windows-specific
- [ ] Back up and restore the clipboard around the synthesized Ctrl+C; **never touch** it when there is no selection.
- [ ] Poll `GetClipboardSequenceNumber` 20ms × 30 — do not hardcode a single delay.
- [ ] Capture the foreground HWND **before** the panel is shown.
- [ ] Vietnamese IME (Unikey or the Windows keyboard) loses no characters; Enter during IME composition does not trigger a translation.
- [ ] There is a global hotkey (`RegisterHotKey`), not just the tray icon.
- [ ] All Accessibility-permission code is removed.
- [ ] TTS handles the case where no voice exists for the target language.
- [ ] Single-instance enforcement (`Mutex`).

### Known gaps (carried over from the mac build as-is)
- [ ] `trialDaysLeft` is hardcoded to `5` — not wired to any entitlement system.
- [ ] The 🕘 icon in the footer is decorative; there is no full history browser.
- [ ] `cached` / `provider` in the response are logged only, never displayed.

---

## 10. Reference: reading the Swift source

If you need to cross-check against the original:

| Swift | C#/WPF equivalent |
|---|---|
| `@Observable` | `INotifyPropertyChanged` (or CommunityToolkit.Mvvm's `[ObservableProperty]`) |
| `@ObservationIgnored` | a plain field that does not raise `PropertyChanged` |
| `@State private var vm` | view model constructed in code-behind, assigned to `DataContext` |
| `@Bindable var vm` | two-way `DataContext` binding |
| `@FocusState` | `Keyboard.Focus()` + the shared state enum (§4.11) |
| `Task { }` + `Task.cancel()` | `async Task` + `CancellationTokenSource` |
| `Task.sleep(nanoseconds: 60_000_000)` | `await Task.Delay(60)` |
| `.help("…")` | `ToolTip="…"` |
| `Menu { }` | `ContextMenu` / `Popup` with an `ItemsControl` |
| `FlowLayout` (hand-rolled) | `WrapPanel` (built in) |
| `LocalizedError` | an `Exception` subclass, or the `TranslationFailure` record union |
| `UserDefaults` | JSON at `%APPDATA%\verba\settings.json` |
| `NSPasteboard.changeCount` | `GetClipboardSequenceNumber()` |
| `AXSelectedText` | `TextPattern.GetSelection()` (UI Automation) |
| `AVSpeechSynthesizer` | `System.Speech.Synthesis.SpeechSynthesizer` |
| `NSStatusItem` | `NotifyIcon` (WinForms) or `H.NotifyIcon.Wpf` |
| `NSPanel` `.nonactivatingPanel` | `WS_EX_NOACTIVATE` |
| `AXIsProcessTrusted()` | (not needed — Windows has no equivalent) |
