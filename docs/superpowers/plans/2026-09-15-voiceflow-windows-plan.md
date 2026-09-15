# VoiceFlow for Windows — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows 11 (x64) tray application with the same flow and capabilities as the macOS VoiceFlow v1.1.0: hold a push-to-talk hotkey → capture the microphone → transcribe locally with whisper.cpp → type the text into the focused application, with zero network calls after the explicit one-time model download. The macOS app must remain byte-for-byte untouched.

**Architecture:** Everything lives under `windows/`. `VoiceFlow.Core` (`net8.0` class library, no Windows APIs) holds settings, the model catalogue/download/checksum, the text-joining rules, the whisper.cpp wrapper (Whisper.net) and the `DictationController` state machine written against injected interfaces, so the whole flow is unit-tested with xUnit — on macOS as well as on Windows. `VoiceFlow.App` (`net8.0-windows`, WinForms) supplies the Win32 adapters (WASAPI capture, `RegisterHotKey`, `SendInput`, `NotifyIcon`, Run key, system sounds) and the tray UI; it compiles on macOS via `EnableWindowsTargeting` and only runs on the user's PC.

**Tech Stack:** C# 12 / .NET 8 SDK, WinForms (`NotifyIcon`, one `SettingsForm`), Whisper.net + Whisper.net.Runtime (CPU) as the whisper.cpp binding, NAudio for WASAPI capture and resampling, xUnit for tests, PowerShell scripts for build/test/run/publish. Reference behaviour: the Swift sources under `Sources/` (read-only).

**Spec:** the macOS design spec `docs/superpowers/specs/2026-09-15-voice-dictation-mac-design.md` is the authority for the flow, failure modes and privacy rules; the platform deviations listed below are the only sanctioned departures.

## Global Constraints

- **Isolation (binding):** only files under `windows/` may be created or changed, plus `CLAUDE.md` (a new "Windows" section) and files under `docs/`. `Package.swift`, `Package.resolved`, `Sources/`, `Tests/`, `Resources/`, `scripts/` and the root `.gitignore` are never touched. After every task `git diff main -- . ':!windows' ':!CLAUDE.md' ':!docs'` must print nothing, and `swift build` + `./scripts/test.sh` must still pass unchanged.
- **Privacy (binding):** no network access anywhere except `ModelManager.DownloadAsync`, which the user triggers from an explicit dialog. No telemetry, analytics, crash reporters, update checks. Any NuGet package that phones home is disqualified.
- **No invented data (binding):** no latency numbers, model sizes beyond the verified byte counts, accuracy claims or benchmark figures in code, comments, UI strings or docs. Checksums are the three values in `Sources/VoiceFlowCore/ModelManager.swift` (verified with `shasum -a 256` on the real files); if a new value is ever needed it is computed, never typed from memory.
- **Same vocabulary as the Mac (binding):** error codes are exactly `microphone-permission`, `audio-start-failed`, `model-missing`, `model-load-failed`, `model-download-failed`, `hotkey-conflict`, `transcription-timeout`, `transcription-failed`, `injection-failed`. There is no `accessibility-permission` on Windows. State names: `Idle`, `Listening`, `Transcribing`, `Error`.
- **Platform deviations (sanctioned):** single fixed push-to-talk hotkey Ctrl+Alt+Space (Windows cannot observe the fn key); text injection by `SendInput` Unicode keystrokes only (no UI Automation writes — see ruling R33 in the Mac ledger: accessibility writes report success without inserting on Chromium); spacing between consecutive dictations uses only the last injected character (`CursorContext.Unavailable` path); no Accessibility permission concept; UIPI means elevated windows cannot receive text — documented, not worked around.
- **Storage:** settings at `%APPDATA%\VoiceFlow\settings.json`; models at `%LOCALAPPDATA%\VoiceFlow\models\<fileName>`. Core classes take paths as constructor parameters; only `VoiceFlow.App` resolves the real Windows folders (`Environment.GetFolderPath`). Tests use temporary directories.
- **Code style:** English identifiers, comments and commit messages; comments only where the WHY is non-obvious; no dead code, no feature flags, no compatibility shims. `Nullable` enabled, warnings not errors (v1).
- **Testing:** xUnit; every Core class gets tests written before the implementation (TDD). No test downloads anything or touches the real `%APPDATA%`. On macOS the command is `dotnet test windows/VoiceFlow.Windows.sln`; the App project has no tests and must not be referenced by the test project.
- **Commits:** one task = one or more commits on `feature/windows`, never on `main`. No pushes by subagents.
- **Human-only verification:** microphone, hotkey, injection, tray and latency are verified by the user on the PC with the checklist produced in Task 9; subagents report those items as "requires human verification — not performed".

---

### Task 1: Solution scaffold and build/test scripts

**Files to create:**
- `windows/.gitignore` — `bin/`, `obj/`, `publish/`, `*.user`, `.vs/`
- `windows/Directory.Build.props` — `<LangVersion>12</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`
- `windows/VoiceFlow.Windows.sln` referencing the three projects
- `windows/VoiceFlow.Core/VoiceFlow.Core.csproj` — `net8.0`, `RootNamespace` `VoiceFlow.Core`, no package references yet
- `windows/VoiceFlow.Core/VoiceFlowCore.cs` — `public static class VoiceFlowCore { public const string Version = "0.1.0"; }`
- `windows/VoiceFlow.App/VoiceFlow.App.csproj` — `net8.0-windows`, `OutputType` `WinExe`, `UseWindowsForms` true, `EnableWindowsTargeting` true, `ApplicationManifest` omitted, project reference to Core
- `windows/VoiceFlow.App/Program.cs` — `[STAThread] static void Main()` that calls `Application.EnableVisualStyles(); Application.SetHighDpiMode(HighDpiMode.SystemAware);` and, for now, `Application.Run(new ApplicationContext())` (placeholder replaced in Task 8)
- `windows/VoiceFlow.Core.Tests/VoiceFlow.Core.Tests.csproj` — xUnit (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, latest stable versions found with `dotnet package search` or `dotnet add package` — record the versions chosen in the report), project reference to Core only
- `windows/VoiceFlow.Core.Tests/VoiceFlowCoreTests.cs` — one test asserting `VoiceFlowCore.Version` is non-empty
- `windows/scripts/build.ps1` — `dotnet build "$PSScriptRoot/../VoiceFlow.Windows.sln" -c Release`
- `windows/scripts/test.ps1` — `dotnet test "$PSScriptRoot/../VoiceFlow.Windows.sln" -c Release`
- `windows/scripts/run.ps1` — `dotnet run --project "$PSScriptRoot/../VoiceFlow.App" -c Release`
- `windows/README.md` — prerequisites on the PC (Git, .NET 8 SDK from dotnet.microsoft.com, `winget install Microsoft.DotNet.SDK.8`), how to clone the private repo, check out `feature/windows`, run the three scripts. Italian text.

**Steps:**
- [ ] Verify `dotnet --version` reports an 8.x SDK on this Mac (installed by the controller; if missing, report BLOCKED).
- [ ] Create the files above; on macOS run `dotnet build windows/VoiceFlow.Windows.sln` — all three projects must compile (the App project through `EnableWindowsTargeting`).
- [ ] Run `dotnet test windows/VoiceFlow.Windows.sln` — 1 test passing.
- [ ] Run `swift build` and `./scripts/test.sh` from the repo root — unchanged (34 tests).
- [ ] Run `git diff main -- . ':!windows' ':!CLAUDE.md' ':!docs'` — must be empty. Note that `windows/bin` and `windows/obj` are ignored (`git status --short` shows no build output).
- [ ] Commit: `feat(windows): scaffold the .NET solution, projects and scripts`

---

### Task 2: TextJoiner port with tests

**Reference:** `Sources/VoiceFlowCore/TextJoiner.swift` and `Tests/VoiceFlowCoreTests/TextJoinerTests.swift` — read both; port the rules and every test case 1:1.

**Files to create:**
- `windows/VoiceFlow.Core/CursorContext.cs`:
  ```csharp
  public abstract record CursorContext
  {
      public sealed record Unavailable : CursorContext;
      public sealed record AtStart : CursorContext;
      public sealed record Character(char Value) : CursorContext;
  }
  ```
- `windows/VoiceFlow.Core/TextJoiner.cs` — `public static class TextJoiner { public static string Prefix(string text, CursorContext precededBy); }` returning `" "` or `""` with exactly the Swift semantics: a space when the previous character is neither whitespace nor an opener (opening brackets/quotes and the straight apostrophe `'`, which is treated as an opener for Italian elision) and the text starts with a letter or digit; `Unavailable` and `AtStart` never get a space.
- `windows/VoiceFlow.Core.Tests/TextJoinerTests.cs` — the Swift test cases, same names in PascalCase, plus the same edge cases.

**Steps:**
- [ ] Write the tests first; run `dotnet test` and confirm they fail to compile/run.
- [ ] Implement `TextJoiner`; run `dotnet test` — all passing.
- [ ] Isolation check as in Task 1.
- [ ] Commit: `feat(windows): port TextJoiner spacing rules with tests`

---

### Task 3: Settings and SettingsStore

**Reference:** `Sources/VoiceFlowCore/SettingsStore.swift`.

**Files to create:**
- `windows/VoiceFlow.Core/Settings.cs`:
  ```csharp
  public enum WhisperModelName { Base, Small, Medium }
  public enum DictationLanguage { Italian, English }   // whisper codes: "it", "en" via extension WhisperCode()
  public sealed record Settings(
      WhisperModelName Model = WhisperModelName.Base,
      DictationLanguage Language = DictationLanguage.Italian,
      bool PlaySounds = true,
      bool LaunchAtLogin = false);
  ```
- `windows/VoiceFlow.Core/SettingsStore.cs` — `public sealed class SettingsStore(string filePath)`; `Settings Load()` returns defaults when the file is missing or unreadable/corrupt (never throws to the caller); `void Save(Settings settings)` writes JSON (`System.Text.Json`, enums as strings, indented) creating the parent directory. Model/language names must round-trip as the lowercase strings `base`/`small`/`medium` and `italian`/`english` (use `JsonStringEnumConverter` with a camel-case naming policy).
- `windows/VoiceFlow.Core.Tests/SettingsStoreTests.cs` — defaults (model Base, language Italian, playSounds true, launchAtLogin false), round trip of every field, missing file → defaults, corrupt file → defaults, parent directory created. Each test uses its own temp directory and deletes it.

**Steps:**
- [ ] Tests first, then implementation, `dotnet test` green.
- [ ] Isolation check. Commit: `feat(windows): add Settings and a JSON SettingsStore with tests`

---

### Task 4: Model catalogue and ModelManager

**Reference:** `Sources/VoiceFlowCore/ModelManager.swift` (catalogue values, streaming SHA-256, download semantics) and `Tests/VoiceFlowCoreTests/ModelManagerTests.swift`.

**Files to create:**
- `windows/VoiceFlow.Core/ModelInfo.cs` — `public sealed record ModelInfo(string Name, string FileName, Uri DownloadUrl, string Sha256);`
- `windows/VoiceFlow.Core/ModelCatalogue.cs` — `public static class ModelCatalogue { public static readonly IReadOnlyList<ModelInfo> KnownModels; public static ModelInfo For(WhisperModelName name); }` with exactly these entries (copied from the Swift file, not retyped from memory — the implementer must open the Swift file and copy):
  - base — `ggml-base.bin` — `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin` — sha256 `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`
  - small — `ggml-small.bin` — `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin` — sha256 `1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b`
  - medium — `ggml-medium.bin` — `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin` — sha256 `6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208`
- `windows/VoiceFlow.Core/ModelManager.cs`:
  ```csharp
  public sealed class ModelManager(string modelsDirectory, HttpMessageHandler? handler = null)
  {
      public string LocalPath(ModelInfo model);
      public bool IsModelPresentAndValid(ModelInfo model);          // exists AND sha256 matches (streaming)
      public static string Sha256OfFile(string path);               // IncrementalHash, 1 MiB chunks, lowercase hex
      public Task DownloadAsync(ModelInfo model, IProgress<double> progress, CancellationToken ct);
  }
  public sealed class ChecksumMismatchException(string expected, string actual) : Exception;
  public sealed class ModelDownloadException(string message, Exception? inner) : Exception;
  ```
  `DownloadAsync`: streams to `<LocalPath>.part` while hashing incrementally, reports `bytesRead / contentLength` (or stays at 0 when the length is unknown), on completion compares the hash — mismatch → delete `.part`, throw `ChecksumMismatchException`; match → move over `LocalPath` (replace); cancellation or HTTP failure → delete `.part`, rethrow as `OperationCanceledException` / `ModelDownloadException`. The `HttpMessageHandler` parameter exists only so tests can inject a fake handler; production passes null and uses a private `HttpClient`.
- `windows/VoiceFlow.Core.Tests/ModelManagerTests.cs` — `Sha256OfFile` against a small fixture with a known hash computed in the test with `SHA256.HashData` (not a literal); `IsModelPresentAndValid` false for missing file, false for wrong hash, true for matching (test uses a fixture `ModelInfo` whose sha256 is computed from the fixture bytes); `DownloadAsync` with a fake `HttpMessageHandler` serving bytes: success writes the file and reports progress reaching 1.0; wrong checksum throws and leaves no `.part`/final file; cancellation mid-stream leaves no `.part`; `ModelCatalogue.KnownModels` has three entries with 64-hex-char checksums and `For()` covers every `WhisperModelName` value.

**Steps:**
- [ ] Tests first, implementation, `dotnet test` green. No test performs network I/O.
- [ ] Isolation check. Commit: `feat(windows): add the model catalogue and a streaming ModelManager with tests`

---

### Task 5: WhisperEngine on Whisper.net

**Reference:** `Sources/VoiceFlowCore/WhisperEngine.swift` (parameters, trim, result shape).

**Steps:**
- [ ] Add `Whisper.net` and `Whisper.net.Runtime` (same version, latest stable found via `dotnet package search Whisper.net` / nuget.org) to `VoiceFlow.Core.csproj`. Record the versions and the whisper.cpp version they bundle (from the package README/release notes) in the report. Inspect `dotnet list package --include-transitive` for Core: if anything network-related appears beyond the BCL, report DONE_WITH_CONCERNS naming it.
- [ ] Create `windows/VoiceFlow.Core/WhisperEngine.cs`:
  ```csharp
  public sealed record TranscriptionResult(string Text, TimeSpan Duration);
  public sealed class WhisperEngineException(string message, Exception? inner = null) : Exception;
  public sealed class WhisperEngine : IDisposable
  {
      public WhisperEngine(string modelPath);   // throws WhisperEngineException if the model cannot be loaded
      public Task<TranscriptionResult> TranscribeAsync(float[] samples16k, string languageCode, CancellationToken ct = default);
      public static string Trim(string text);   // same rule as the Swift trim (whitespace/newlines, collapse leading/trailing)
  }
  ```
  Use `WhisperFactory.FromPath(modelPath)` and `factory.CreateBuilder().WithLanguage(languageCode).Build()`; feed `samples16k` with the processor's `float[]`/`ReadOnlyMemory<float>` overload of `ProcessAsync` if the installed version has one (verify against the package's public API with `dotnet` reflection or the package XML docs — do not guess); otherwise wrap the samples in an in-memory 16 kHz mono 16-bit WAV stream. Concatenate segment texts in order, `Trim`, measure wall-clock `Duration` with `Stopwatch`.
- [ ] `windows/VoiceFlow.Core.Tests/WhisperEngineTests.cs` — unit tests for `Trim` only, plus one integration test marked with a skippable condition: runs only when the environment variable `VOICEFLOW_TEST_MODEL` points to an existing ggml file (on this Mac: `~/Library/Application Support/VoiceFlow/models/ggml-base.bin`); it transcribes 1 s of silence (zeros) and asserts no exception and a string result (content unspecified — silence may yield empty text or a hallucinated token; the test asserts only that the pipeline runs). If Whisper.net.Runtime has no macOS native binary, the integration test reports skipped and the report says "not executed on macOS".
- [ ] `dotnet test` green (integration skipped or passed — state which). Isolation check.
- [ ] Commit: `feat(windows): add WhisperEngine on Whisper.net`

---

### Task 6: DictationController state machine

**Reference:** `Sources/VoiceFlowApp/StatusBarController.swift` — `beginDictation()`, `finishDictation()`, the 15 s timeout with "late result dropped" semantics, `inject(_:)` with the last-injected-character memory, and the error codes. Read it fully.

**Files to create:**
- `windows/VoiceFlow.Core/Ports.cs`:
  ```csharp
  public interface IAudioCapture { void Start(); float[] Stop(); }              // Start throws AudioCaptureException(code) with code "microphone-permission" or "audio-start-failed"
  public sealed class AudioCaptureException(string code, string message, Exception? inner = null) : Exception { public string Code { get; } }
  public interface IHotkey { event Action? Pressed; event Action? Released; bool Register(); void Unregister(); }
  public interface ITextInjector { bool Inject(string text); }
  public interface ISoundPlayer { void PlayStart(); void PlayStop(); }
  public interface ITranscriber { Task<TranscriptionResult> TranscribeAsync(float[] samples16k, string languageCode, CancellationToken ct); }  // WhisperEngine implements it
  public interface IScheduler { IDisposable Schedule(TimeSpan delay, Action action); Task RunAsync(Func<Task> work); }  // real: Task.Delay / Task.Run marshalled back; tests: manual
  ```
- `windows/VoiceFlow.Core/DictationController.cs`:
  ```csharp
  public enum DictationState { Idle, Listening, Transcribing, Error }
  public sealed class DictationController
  {
      public DictationController(IAudioCapture audio, IHotkey hotkey, ITextInjector injector, ISoundPlayer sounds, IScheduler scheduler, Func<Settings> settings, Func<ITranscriber?> transcriber);
      public DictationState State { get; }
      public string? LastErrorCode { get; }
      public event Action<DictationState>? StateChanged;
      public event Action<string>? ErrorRaised;       // fired once per transition into Error, with the code
      public bool Start();                            // registers the hotkey; false → state Error("hotkey-conflict")
      public void Stop();
      public static readonly TimeSpan TranscriptionTimeout = TimeSpan.FromSeconds(15);
  }
  ```
  Behaviour (mirror the Mac exactly): press while Listening/Transcribing is ignored; press from Idle/Error → if `transcriber()` is null → Error("model-missing"); else PlayStart (if `PlaySounds`), `audio.Start()` (exception → Error(code)), state Listening. Release while Listening → `samples = audio.Stop()`, PlayStop, state Transcribing, schedule the timeout, run transcription on the scheduler; when it completes: if timed out already → drop the result silently; else cancel the timeout, if the text is empty → Idle; else `Inject` with `TextJoiner.Prefix(text, lastInjected is null ? Unavailable : Character(lastInjected))` → false → Error("injection-failed"); true → remember the last character, Idle. Transcription exception → Error("transcription-failed"). Timeout fires while Transcribing → Error("transcription-timeout"). Release while not Listening → ignored. All state changes happen through the scheduler's marshalling so callers see them on one thread.
- `windows/VoiceFlow.Core.Tests/DictationControllerTests.cs` with hand-written fakes (no mocking library): every transition above, press ignored while busy, empty transcription → Idle with nothing injected, spacing between two consecutive dictations ("siamo!" then "Adesso" → second inject receives " Adesso"), timeout → Error and a late result is dropped (no inject), each error code, sounds not played when `PlaySounds` is false, hotkey conflict on `Start()`.

**Steps:**
- [ ] Tests first, then implementation, `dotnet test` green. Isolation check.
- [ ] Commit: `feat(windows): add the DictationController state machine with tests`

---

### Task 7: Windows adapters

**Reference:** `Sources/VoiceFlowCore/AudioCapture.swift` (16 kHz mono float, thread-safe buffer), `HotkeyManager.swift` (conflict → false), `TextInjector.swift` (`insertViaSyntheticKeystrokes` returns false on any failure), `Sources/VoiceFlowApp/SoundPlayer.swift`, `SettingsView.swift` (launch at login).

**Files to create in `windows/VoiceFlow.App/`:**
- `Native/NativeMethods.cs` — P/Invoke declarations only: `RegisterHotKey`, `UnregisterHotKey`, `GetAsyncKeyState`, `SendInput` with the `INPUT`/`KEYBDINPUT` structs, constants `MOD_CONTROL=0x0002`, `MOD_ALT=0x0001`, `MOD_NOREPEAT=0x4000`, `VK_SPACE=0x20`, `VK_CONTROL=0x11`, `VK_MENU=0x12`, `WM_HOTKEY=0x0312`, `KEYEVENTF_UNICODE=0x0004`, `KEYEVENTF_KEYUP=0x0002`, `INPUT_KEYBOARD=1`.
- `WasapiAudioCapture.cs : IAudioCapture` — NAudio `WasapiCapture` on the default capture device (shared mode); on `DataAvailable` convert the device format to 16 kHz mono float via `MediaFoundationResampler` (or `WdlResamplingSampleProvider` if MediaFoundation is unavailable — state which in the report) and append to a `List<float>` under a lock; `Start()` maps `COMException`/`UnauthorizedAccessException` with the WASAPI access-denied HRESULT (`0x80070005`) to `AudioCaptureException("microphone-permission")` and any other failure (no device, init error) to `AudioCaptureException("audio-start-failed")`; `Stop()` stops the capture and returns the buffered samples. Add the NAudio package (latest stable, record the version).
- `GlobalHotkey.cs : IHotkey` — a hidden `NativeWindow` (message-only) receiving `WM_HOTKEY`; `Register()` calls `RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE)` and returns its result; on `WM_HOTKEY` raise `Pressed` and start a 16 ms `System.Windows.Forms.Timer` that polls `GetAsyncKeyState` for `VK_SPACE`, `VK_CONTROL`, `VK_MENU` and raises `Released` (once) as soon as any of them is up, then stops the timer; `Unregister()` stops the timer and unregisters. Comment the WHY: `WM_HOTKEY` has no key-up message.
- `SendInputTextInjector.cs : ITextInjector` — build one `INPUT` pair (down/up, `KEYEVENTF_UNICODE`) per UTF-16 code unit (surrogate pairs are sent as two units, which Windows reassembles), send in chunks of 64 events, return false if any `SendInput` call returns fewer events than passed (`Marshal.GetLastWin32Error()` in a comment-free local for debugging is fine).
- `SystemSoundPlayer.cs : ISoundPlayer` — `System.Media.SystemSounds.Asterisk.Play()` for start, `SystemSounds.Beep.Play()` for stop (no assets).
- `StartupRegistration.cs` — `static bool IsEnabled()`, `static void SetEnabled(bool)` on `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `VoiceFlow`, data `"<path to the running exe>"` quoted; throws on registry failure (the UI reports it and reverts the toggle, like the Mac's R21).
- `WindowsPaths.cs` — `static string SettingsFile` (`%APPDATA%\VoiceFlow\settings.json`), `static string ModelsDirectory` (`%LOCALAPPDATA%\VoiceFlow\models`).
- `ThreadScheduler.cs : IScheduler` — `Schedule` via a `System.Windows.Forms.Timer` (UI thread), `RunAsync` via `Task.Run` with continuation posted back through a captured `SynchronizationContext`.

**Steps:**
- [ ] `dotnet build` on macOS must compile all of the above (no runtime possible here). Keep each class small; no UI in this task.
- [ ] Isolation check. Report every item as "requires human verification on the PC — not performed".
- [ ] Commit: `feat(windows): add the WASAPI, hotkey, SendInput, sound, startup and scheduler adapters`

---

### Task 8: Tray application, settings form and dialogs

**Reference:** `Sources/VoiceFlowApp/StatusBarController.swift` (`presentAlert(for:)`, download flow with progress and Cancel, rulings R22/R23: no stacked alerts, stale completions ignored, `.idle` only if the engine loaded) and `SettingsView.swift`.

**Files to create in `windows/VoiceFlow.App/`:**
- `TrayIcons.cs` — `static Icon For(DictationState state)`: draw a 16×16 (and 32×32 for high DPI) circle with GDI+ — grey for Idle, red for Listening, blue for Transcribing, orange for Error; cache the icons; no image files.
- `SettingsForm.cs` — a small fixed-size form: model `ComboBox` (Base (faster) / Small (more accurate) / Medium (most accurate, slower)), language `ComboBox` (Italiano / English), `CheckBox` "Play sounds", `CheckBox` "Launch at login" (reverts and shows the error text in a red label if `StartupRegistration.SetEnabled` throws), a "Quit VoiceFlow" button, and a static note: "Push-to-talk: hold Ctrl+Alt+Space". Changes are saved through `SettingsStore` immediately; changing the model calls back to the app so it reloads/downloads.
- `TrayApp.cs : ApplicationContext` — composition root: builds `SettingsStore`, `ModelManager`, the adapters, the `DictationController`; `NotifyIcon` with the state icon, tooltip "VoiceFlow", left click opens `SettingsForm`, context menu with Settings… and Quit. Subscribes to `StateChanged` (icon) and `ErrorRaised` (dialog). Model lifecycle: on start and on model change, if `ModelManager.IsModelPresentAndValid` → construct `WhisperEngine` (failure → Error("model-load-failed") dialog offering re-download); else Error("model-missing") dialog with Download/Cancel; Download shows a modal progress form with a `ProgressBar` and Cancel, runs `DownloadAsync`, on success reloads the engine, on `ChecksumMismatchException`/`ModelDownloadException` shows the failure dialog. Exactly one dialog at a time (`isPresentingDialog` guard, restored after nesting as in R23); a download completion for a cancelled/stale request is ignored (`activeDownloadId`).
- `ErrorDialogs.cs` — `static void Show(string code, IWin32Window owner, Action? retryDownload)`: one message per code, English, plain: `microphone-permission` → text plus a button that opens `ms-settings:privacy-microphone` via `Process.Start(new ProcessStartInfo(...) { UseShellExecute = true })`; `hotkey-conflict` → "Ctrl+Alt+Space is already registered by another application. Quit that app or free the shortcut there, then relaunch VoiceFlow."; the others mirror the Mac wording in `presentAlert(for:)`.
- `Program.cs` — replace the placeholder with a single-instance guard (`Mutex` named `Local\VoiceFlow.SingleInstance`; a second launch exits silently) and `Application.Run(new TrayApp())`.

**Steps:**
- [ ] `dotnet build` on macOS compiles. No behaviour can be run here: report all UI items as "requires human verification on the PC — not performed".
- [ ] Isolation check. Commit: `feat(windows): add the tray application, settings form and error dialogs`

---

### Task 9: Publish script, docs and the human checklist

**Files:**
- `windows/scripts/publish.ps1` — `dotnet publish "$PSScriptRoot/../VoiceFlow.App" -c Release -r win-x64 --self-contained false -o "$PSScriptRoot/../publish"`; print the output folder and the note that the .NET 8 Desktop Runtime must be installed once (`winget install Microsoft.DotNet.DesktopRuntime.8`).
- `windows/README.md` — extend Task 1's file: full first-run walkthrough on the PC (SmartScreen "More info → Run anyway" for an unsigned exe is expected), the hotkey, where settings and models live, the known limitations (elevated windows/UIPI, no fn key, SendInput may be flagged by some antivirus), the privacy statement (only network call = model download; verify with Wi-Fi off).
- `CLAUDE.md` — append a "## Windows" section (Italian): where the Windows code lives, that it is a separate .NET solution, the isolation rule (`git diff main -- . ':!windows' ':!CLAUDE.md' ':!docs'` empty), the scripts, and that the Windows checklist is human-only. Do not edit any existing section.
- `docs/superpowers/verification/2026-09-15-windows-manual-checklist.md` (Italian, same shape as the Mac checklist): prerequisites; first launch → model-missing dialog → download with progress → engine loads; Ctrl+Alt+Space in Notepad → text; two consecutive dictations → spacing; VS Code (Electron) and a browser text field; tray icon states; sounds on/off; model and language persist after relaunch; launch at login visible in Task Manager → Startup apps; Wi-Fi off → dictation still works; hotkey conflict (register Ctrl+Alt+Space in another tool) → dialog; corrupt the model file → model-load-failed dialog → re-download; elevated window → no text (known limit); a line per model to write the observed release-to-text delay by hand ("misurato: ___ s") — no prefilled numbers.

**Steps:**
- [ ] `dotnet build` still green; isolation check (only `windows/`, `CLAUDE.md`, `docs/` changed).
- [ ] Commit: `docs(windows): publish script, README, CLAUDE.md section and the human checklist`

---

### Task 10: PC verification round (human) and fixes

This task is executed by the controller with the user, not by a subagent: the user runs the checklist on the PC and reports; findings become fix dispatches (one implementer per batch of findings, reviewed as usual). Any latency values are written by the user into the checklist. The final whole-branch review follows.
