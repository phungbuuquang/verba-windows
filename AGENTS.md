# Project Instructions

## Scope

These instructions apply to the entire repository. Add a nested `AGENTS.md` only when a subdirectory needs more specific guidance.

## Project Overview

Verba for Windows is a Windows-only .NET 9 WPF tray application. A global shortcut opens a translation panel, captures the current selection through UI Automation with a clipboard fallback, calls the translation service, and supports refinement history and text-to-speech.

- Treat `PORTING-WINDOWS.md` as the normative product, API, state-machine, UI, persistence, and localization specification.
- Keep the existing MVVM boundaries: UI and window behavior in `App/` and XAML, state and commands in `ViewModels/`, external and Windows integrations in `Services/`, data contracts in `Models/`, and interop/helpers in `Utilities/`.
- The root project intentionally excludes `Tests/**/*.cs`; tests are compiled by the separate project in `Tests/`.

## Build and Test

Run commands from the repository root on Windows with the .NET 9 SDK.

```powershell
dotnet build verba-windows.csproj
dotnet run --project Tests\verba-windows.Tests.csproj
```

- The test project is a custom console regression harness, not a test-SDK project; do not replace the test command with `dotnet test` unless the project is deliberately migrated.
- Run build and test commands sequentially because both compile the root project and can contend for `obj/` files.
- A running `verba-windows.exe` locks the build output. Close only an instance started for the current task; do not terminate a pre-existing user instance without permission.
- For UI smoke testing, run `dotnet run --project verba-windows.csproj`, then verify tray startup, the global shortcut, selection capture, panel focus/positioning, translation, clipboard restoration, and speech as relevant to the change.

## Implementation Rules

- Preserve the API contract in section 2 of `PORTING-WINDOWS.md`, including the endpoint, JSON names, null-versus-omitted fields, history shape, response validation, error-envelope precedence, and failure classification. Add or update contract tests when touching it.
- Preserve the translation state machine in `TranslationViewModel`: a newer request cancels the older one, cancellation is not a user-visible failure, translating state gates conflicting actions, and a new result after undo truncates the redo branch.
- Keep network, selection-capture, and speech work asynchronous. Do not block the WPF dispatcher thread.
- Preserve selection capture order: try UI Automation first, then the Ctrl+C fallback. The fallback must restore the user's clipboard as faithfully as possible.
- Put model-facing tone/refinement text in API instructions, not localized display strings. Keep preset API values and other server-facing tokens stable.
- Route user-visible text through `Utilities/Strings.cs`. When adding UI copy, update every supported interface language and preserve the fixed translation-language list unless the specification changes.
- Keep nullable reference types enabled and follow the existing file-scoped namespace and C# style. Prefer focused changes over unrelated rewrites.
- Do not add a production dependency, change the production endpoint, or alter persisted settings compatibility without calling out the reason and impact first.

## Tests and Verification

- Add regression coverage to `Tests/Program.cs` for behavioral changes. Keep tests deterministic and avoid real network, clipboard, speech, or global-hotkey dependencies when a fake can cover the logic.
- At minimum, run the console regression harness after changing models, services, settings, shortcut parsing, speech behavior, or `TranslationViewModel`.
- Build the WPF project after XAML, app lifecycle, tray, window, or Windows interop changes. Manually verify OS-integrated behavior that the console harness cannot exercise.
- Report any verification skipped because Windows UI interaction or a running app blocked it.

## Code Review Rules

- Flag changes that diverge from `PORTING-WINDOWS.md` without an explicit product decision.
- Flag API changes that alter serialization, error classification, cancellation, or history semantics without regression coverage.
- Flag clipboard fallback changes that can destroy clipboard contents or leave focus on the wrong application.
- Flag new user-visible strings that bypass localization or are missing from any supported interface language.
