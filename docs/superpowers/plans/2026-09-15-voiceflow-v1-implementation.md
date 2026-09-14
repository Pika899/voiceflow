# VoiceFlow v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build VoiceFlow v1 — a macOS menu bar app that does fully local push-to-talk voice dictation (whisper.cpp) and types the result into whatever app has focus, with zero network calls after the one-time model download.

**Architecture:** A local Swift Package (`VoiceFlowCore`) holds all non-UI logic — hotkey capture, audio capture/resampling, the whisper.cpp wrapper, model download/checksum, settings storage, permissions, and text injection — each as an independently testable unit. A standalone `LatencySpike` executable target wires the first three of these together to validate the CPU-latency risk *before* any UI is built. Once the spike confirms a usable model tier, a second SPM executable target (`VoiceFlowApp`) depends on `VoiceFlowCore` and adds the menu bar UI, settings popover, and end-to-end error handling; a build script assembles it into a real `.app` bundle (`LSUIElement`, `Info.plist`) for the user to run.
>
> **Ruling (recorded 2026-09-15, before Task 1 dispatch):** this machine has only the Xcode Command Line Tools installed, not Xcode.app (`xcodebuild` fails: "requires Xcode"; `/Applications` has no Xcode). The plan as originally written assumed an Xcode-GUI-created `.xcodeproj` for Task 10 — that path is not executable by an automated subagent (no GUI) nor by this machine (no Xcode.app) as it stands. Ruling: `VoiceFlowApp` ships as a plain SPM `executableTarget` alongside `LatencySpike`, with a hand-written `Info.plist` under `Resources/VoiceFlowApp/` and a `scripts/package-app.sh` that runs `swift build -c release`, assembles `dist/VoiceFlow.app`, and ad-hoc codesigns it. This is a strictly more automatable, non-GUI equivalent of what Task 10 originally specified — same target name, same files, same wiring — and it directly produces the `.app` executable requested for manual testing. If Xcode.app is installed later, `swift package generate-xcodeproj`-style tooling or a manually authored `.xcodeproj` can be added without touching `VoiceFlowCore` or any other task's code. Cost if wrong: the app runs unsigned/ad-hoc rather than through a "real" Xcode scheme — acceptable for v1's stated scope (no notarization, no distribution outside this Mac).

**Tech Stack:** Swift 5.9+, Swift Package Manager, AVFoundation (`AVAudioEngine`, `AVAudioConverter`), whisper.cpp (via its own SPM package), the `HotKey` SPM package (soffes/HotKey) for global hotkeys, ApplicationServices (`AXUIElement`) + CoreGraphics (`CGEvent`) for text injection, `ServiceManagement` (`SMAppService`) for launch-at-login, XCTest for unit tests, macOS 13+ (Ventura) as the platform floor.

**Spec:** `docs/superpowers/specs/2026-09-15-voice-dictation-mac-design.md`

## Global Constraints

- Zero network calls in the dictation flow, ever — the only network call in the entire app is the one-time model download, and it must show a visible progress bar (spec: "Gestione errori", "Architettura").
- No telemetry, no analytics, no third-party crash reporting. Logging, if any, stays local to disk (project CLAUDE.md: "Privacy — non negoziabile").
- No invented metrics, benchmarks, or checksums anywhere in code, comments, or docs. A number that hasn't been measured is stated as an estimate or left as "to measure" (project CLAUDE.md: "Niente dati inventati").
- Code, comments, variable names, and commit messages: English. Conversation and docs produced for the user: Italian (user CLAUDE.md: "Lingua").
- The model file is never bundled in the app binary; it is downloaded on first run into `~/Library/Application Support/VoiceFlow/models/` (project CLAUDE.md, spec: "ModelManager").
- The project folder path contains a space (`.../AGENCY /VOICE APP`) — always quote it in shell commands.
- `LSUIElement = true` in the app's `Info.plist` — no Dock icon, no main window.
- Platform floor: macOS 13 (Ventura), required for `SMAppService` (launch-at-login).
- No unattended automation ships without error handling (user CLAUDE.md: "Consegna") — this is why Task 12 exists as a dedicated task, not an afterthought.
- Explicitly out of scope for every task below: LLM text cleanup/rewriting, personal dictionary, snippets, per-app style, multi-device sync, 100+ language support, Windows/Linux, code signing/notarization, billing (spec: "Fuori scope").
- No usage caps of any kind — no word limit, no weekly quota, no dictation-time limit. This is a deliberate product difference from Wispr Flow (whose free tier caps usage at ~2000 words/week): since there's no server and no billing, there is nothing to meter, and no task in this plan should add metering/quota logic.

---

## File Structure

```
VOICE APP/                              (repo root)
  Package.swift                         # SPM package: VoiceFlowCore lib, LatencySpike + VoiceFlowApp exes, test target
  Sources/
    VoiceFlowCore/
      HotkeyManager.swift                # Task 4
      AudioCapture.swift                 # Task 3
      WhisperEngine.swift                # Task 2
      ModelManager.swift                 # Task 6
      SettingsStore.swift                # Task 7
      PermissionsManager.swift           # Task 8
      TextInjector.swift                 # Task 9
    LatencySpike/
      main.swift                         # Task 5
    VoiceFlowApp/                        # Task 10 — plain SPM executable target, no .xcodeproj
      VoiceFlowApp.swift                 # Task 10 — @main, AppDelegate, LSUIElement
      StatusBarController.swift          # Task 10
      SettingsView.swift                 # Task 11
  Resources/
    VoiceFlowApp/
      Info.plist                         # Task 10 — hand-written, copied into the .app bundle by the packaging script
  Tests/
    VoiceFlowCoreTests/
      WhisperEngineTests.swift           # Task 2
      AudioCaptureTests.swift            # Task 3
      ModelManagerTests.swift            # Task 6
      SettingsStoreTests.swift           # Task 7
  scripts/
    package-app.sh                       # Task 10 — builds release binary and assembles dist/VoiceFlow.app
```

`VoiceFlowCore` has no UI dependencies (AppKit types like `NSEvent.ModifierFlags` are fine since the whole app is macOS-only), so every file in it is `./scripts/test.sh`-able without Xcode. `VoiceFlowApp` is a plain SPM executable target (per the ruling above, this machine has no Xcode.app) that only wires these pieces to `NSStatusItem`/SwiftUI — it stays thin by design, per "Files that change together should live together." `scripts/package-app.sh` turns the built binary into a real, launchable `VoiceFlow.app`.

---

## Task 1: Repo scaffold and vendored whisper.cpp

> **Ruling R10 (2026-09-15):** upstream whisper.cpp no longer ships a `Package.swift` (404 on `master`; the last tag with one, v1.7.4, is only a `pkgConfig` systemLibrary that links a prebuilt system library rather than building from source), and `ggml-org/whisper.spm` is stale (newest tag 1.6.2, Metal disabled — which would make Task 5's latency number unrepresentative on this arm64 Mac). So whisper.cpp is **vendored at pinned tag v1.9.4 and built from source into static libraries** by a committed script, then linked into `VoiceFlowCore`. Static, not Homebrew-dylib, so the shipped `.app` has no runtime dependency on Homebrew.

**Files:**
- Create: `Package.swift`
- Create: `scripts/build-whisper.sh`
- Create: `Sources/CWhisper/module.modulemap`
- Create: `Sources/VoiceFlowCore/VoiceFlowCore.swift`
- Create: `Sources/LatencySpike/main.swift`
- Create: `Tests/VoiceFlowCoreTests/PackageScaffoldTests.swift`
- Modify: `.gitignore` (ignore `Vendor/`)

**Interfaces:**
- Produces: a buildable `VoiceFlowCore` library target that can `import CWhisper` and call whisper.cpp's C API (`whisper_init_from_file`, `whisper_full`, …), plus a `LatencySpike` executable target. Task 2 consumes `CWhisper` directly.

- [ ] **Step 1: Write the whisper.cpp vendoring script**

`scripts/build-whisper.sh`:

```bash
#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

WHISPER_TAG="v1.9.4"
SRC_DIR="Vendor/whisper.cpp-src"
OUT_DIR="Vendor/whisper"

if ! command -v cmake >/dev/null 2>&1; then
    echo "cmake is required to build whisper.cpp. Install it with: brew install cmake"
    exit 1
fi

if [ ! -d "$SRC_DIR" ]; then
    echo "Cloning whisper.cpp $WHISPER_TAG..."
    git clone --depth 1 --branch "$WHISPER_TAG" https://github.com/ggml-org/whisper.cpp "$SRC_DIR"
fi

echo "Building whisper.cpp (static, Metal embedded)..."
cmake -S "$SRC_DIR" -B "$SRC_DIR/build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0 \
    -DBUILD_SHARED_LIBS=OFF \
    -DGGML_METAL=ON \
    -DGGML_METAL_EMBED_LIBRARY=ON \
    -DGGML_ACCELERATE=ON \
    -DWHISPER_BUILD_TESTS=OFF \
    -DWHISPER_BUILD_EXAMPLES=OFF \
    -DCMAKE_INSTALL_PREFIX="$(pwd)/$OUT_DIR"

cmake --build "$SRC_DIR/build" --config Release -j"$(sysctl -n hw.ncpu)"
cmake --install "$SRC_DIR/build"

echo "Static libraries installed in $OUT_DIR/lib:"
ls -1 "$OUT_DIR/lib"
echo "Headers installed in $OUT_DIR/include:"
ls -1 "$OUT_DIR/include"
```

```bash
chmod +x scripts/build-whisper.sh
```

> `GGML_METAL_EMBED_LIBRARY=ON` compiles the Metal shaders into the binary so there is no runtime `.metallib` to locate inside the `.app` bundle. `BUILD_SHARED_LIBS=OFF` gives static archives, so the app carries whisper.cpp inside its own executable.

- [ ] **Step 2: Run the vendoring script and record what it actually produced**

```bash
brew install cmake     # build-time only; not needed at runtime
./scripts/build-whisper.sh
```

Expected: `Vendor/whisper/include/whisper.h` exists, and `Vendor/whisper/lib/` contains `libwhisper.a` plus the `libggml*.a` family. **Write down the exact library filenames the script printed** — Step 4's linker flags must list the libraries that actually exist, never a guessed set.

- [ ] **Step 3: Write the module map exposing whisper.h to Swift**

`Sources/CWhisper/module.modulemap`:

```
module CWhisper {
    header "../../Vendor/whisper/include/whisper.h"
    export *
}
```

- [ ] **Step 4: Create `Package.swift` at the repo root**

```swift
// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "VoiceFlow",
    // macOS 14, not 13: the Testing.framework shipped with the Command Line
    // Tools is built for 14.0, and linking it into a 13.0 target warns.
    // v1 is local-only, so raising the floor costs nothing.
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "VoiceFlowCore", targets: ["VoiceFlowCore"]),
        .executable(name: "LatencySpike", targets: ["LatencySpike"])
    ],
    dependencies: [
        .package(url: "https://github.com/soffes/HotKey", from: "0.2.1")
    ],
    targets: [
        .systemLibrary(name: "CWhisper", path: "Sources/CWhisper"),
        .target(
            name: "VoiceFlowCore",
            dependencies: [
                "CWhisper",
                .product(name: "HotKey", package: "HotKey")
            ],
            linkerSettings: [
                .unsafeFlags([
                    "-LVendor/whisper/lib",
                    "-lwhisper",
                    "-lggml",
                    "-lggml-base",
                    "-lggml-cpu",
                    "-lggml-blas",
                    "-lggml-metal"
                ]),
                .linkedFramework("Accelerate"),
                .linkedFramework("Metal"),
                .linkedFramework("MetalKit"),
                .linkedFramework("Foundation")
            ]
        ),
        .executableTarget(
            name: "LatencySpike",
            dependencies: ["VoiceFlowCore"]
        ),
        .testTarget(
            name: "VoiceFlowCoreTests",
            dependencies: ["VoiceFlowCore"]
        )
    ]
)
```

> The `-l` list above must match the archives Step 2 actually produced — adjust it to the real filenames (`libggml-cpu.a` → `-lggml-cpu`, and so on), dropping any that don't exist and adding any that do. `.unsafeFlags` is permitted here because VoiceFlow is the root package, not a consumed dependency. `-LVendor/whisper/lib` is relative, so `swift build` must be run from the package root.

- [ ] **Step 5: Create the library's first source file**

`Sources/VoiceFlowCore/VoiceFlowCore.swift`:

```swift
/// Version of the VoiceFlowCore library.
public let voiceFlowCoreVersion = "1.0.0"
```

(SwiftPM refuses to build a target with no Swift sources, so this file is what makes the library target valid until Task 2 adds real code.)

- [ ] **Step 6: Create a placeholder executable entry point**

`Sources/LatencySpike/main.swift`:

```swift
print("VoiceFlow latency spike — scaffold OK")
```

- [ ] **Step 7: Write a test that proves whisper.cpp is actually linked**

`Tests/VoiceFlowCoreTests/PackageScaffoldTests.swift`:

```swift
import Testing
import CWhisper
@testable import VoiceFlowCore

@Suite struct PackageScaffoldTests {
    @Test func coreVersionIsExposed() {
        #expect(voiceFlowCoreVersion == "1.0.0")
    }

    @Test func whisperLibraryIsLinked() {
        // whisper_print_system_info returns a C string describing the build.
        // Calling it proves the vendored static library is linked and callable.
        let info = String(cString: whisper_print_system_info())
        #expect(!info.isEmpty)
    }
}
```

> **Ruling R13:** tests use **Swift Testing** (`import Testing`, `@Test`, `#expect`), not XCTest. XCTest ships only inside Xcode.app and this machine has Command Line Tools only — `xcrun --find xctest` fails and no `XCTest.framework` exists on disk. Swift Testing does ship with the CLT (`/Library/Developer/CommandLineTools/Library/Developer/Frameworks/Testing.framework`) and was verified working here by building and running a throwaway package: `./scripts/test.sh` reported "1 test passed". This keeps the agreed testing strategy intact — automated tests for isolatable logic, manual verification for system integration — with the framework that actually exists in this environment.

- [ ] **Step 8: Ignore the vendored sources**

Add to `.gitignore`:

```
Vendor/
```

The vendoring script is committed; the multi-hundred-megabyte checkout and build output are not. Anyone cloning the repo runs `./scripts/build-whisper.sh` once.

- [ ] **Step 9: Build and run**

Run: `swift build`
Expected: resolves HotKey, compiles, links against the vendored static libraries.

Run: `./scripts/test.sh`
Expected: 2 tests pass, including `testWhisperLibraryIsLinked`.

Run: `swift run LatencySpike`
Expected: prints `VoiceFlow latency spike — scaffold OK`.

- [ ] **Step 10: Commit**

```bash
git add Package.swift Package.resolved Sources Tests scripts/build-whisper.sh .gitignore
git commit -m "chore: scaffold VoiceFlowCore package with vendored whisper.cpp"
```

---

## Task 2: WhisperEngine — whisper.cpp wrapper

**Files:**
- Create: `Sources/VoiceFlowCore/WhisperEngine.swift`
- Test: `Tests/VoiceFlowCoreTests/WhisperEngineTests.swift`

**Interfaces:**
- Consumes: the `CWhisper` module map target over the vendored whisper.cpp headers (Task 1).
- Produces: `WhisperEngine(modelPath: String) throws`, `func transcribe(samples: [Float], language: String) throws -> TranscriptionResult`, `TranscriptionResult { text: String, durationSeconds: Double }`, `WhisperEngineError`. Task 5 (spike) and Task 10 (app) both call `transcribe(samples:language:)`.

- [ ] **Step 1: Write the failing test for the pure post-processing logic**

The actual whisper.cpp inference needs a real `.bin` model file (100+ MB, not something to fetch in a unit test), so only the trimming logic is unit-tested here; the full inference path is verified manually in Task 5's spike run.

```swift
import Testing
@testable import VoiceFlowCore

@Suite struct WhisperEngineTests {
    @Test func trimRemovesLeadingAndTrailingWhitespace() {
        #expect(WhisperEngine.trim("  ciao mondo  \n") == "ciao mondo")
    }

    @Test func trimOfEmptyStringIsEmpty() {
        #expect(WhisperEngine.trim("") == "")
    }

    @Test func trimOfWhitespaceOnlyIsEmpty() {
        #expect(WhisperEngine.trim("   \n\t ") == "")
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./scripts/test.sh --filter WhisperEngineTests`
Expected: FAIL — `WhisperEngine` does not exist yet.

- [ ] **Step 3: Write the implementation**

`Sources/VoiceFlowCore/WhisperEngine.swift`:

```swift
import Foundation
import CWhisper

public struct TranscriptionResult {
    public let text: String
    public let durationSeconds: Double
}

public enum WhisperEngineError: Error {
    case modelLoadFailed(path: String)
    case inferenceFailed
}

public final class WhisperEngine {
    private let context: OpaquePointer

    public init(modelPath: String) throws {
        // `whisper_init_from_file` is deprecated in whisper.cpp v1.9.4; the
        // params variant is the supported entry point, and its default
        // `use_gpu` lets inference reach Metal on Apple Silicon.
        let contextParams = whisper_context_default_params()
        guard let ctx = whisper_init_from_file_with_params(modelPath, contextParams) else {
            throw WhisperEngineError.modelLoadFailed(path: modelPath)
        }
        self.context = ctx
    }

    deinit {
        whisper_free(context)
    }

    public func transcribe(samples: [Float], language: String) throws -> TranscriptionResult {
        // Nothing captured (hotkey tapped without speaking): don't hand
        // whisper a null buffer, just report an empty transcription.
        guard !samples.isEmpty else {
            return TranscriptionResult(text: "", durationSeconds: 0)
        }

        let start = Date()
        var params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY)
        params.print_progress = false
        params.print_realtime = false

        let status: Int32 = language.withCString { langPtr in
            params.language = langPtr
            return samples.withUnsafeBufferPointer { buffer in
                whisper_full(context, params, buffer.baseAddress, Int32(buffer.count))
            }
        }
        guard status == 0 else { throw WhisperEngineError.inferenceFailed }

        let segmentCount = whisper_full_n_segments(context)
        var text = ""
        for i in 0..<segmentCount {
            // A null segment after a successful whisper_full is a whisper.cpp
            // anomaly; surface it rather than returning quietly truncated text.
            guard let cText = whisper_full_get_segment_text(context, i) else {
                throw WhisperEngineError.inferenceFailed
            }
            text += String(cString: cText)
        }

        return TranscriptionResult(
            text: Self.trim(text),
            durationSeconds: Date().timeIntervalSince(start)
        )
    }

    static func trim(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
```

> The `language` C string must stay alive for the duration of `whisper_full` — that's why `whisper_full` is called *inside* the `withCString` closure, not after it returns.

- [ ] **Step 4: Run test to verify it passes**

Run: `./scripts/test.sh --filter WhisperEngineTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Manual verification of real inference (not automated)**

Download a real `ggml-base.bin` manually for this check only (Task 6 automates this for the shipped app):

```bash
curl -L -o /tmp/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
```

Add a temporary `print` in `LatencySpike/main.swift` that loads this model, feeds it a short silent or spoken sample (any `[Float]` array — even 1 second of silence at 16kHz, i.e. 16000 zeros, is enough to confirm it doesn't crash and returns without error), and prints the result. Confirm no crash and `status == 0`. Remove the temporary code before continuing (Task 5 builds the real version of this).

- [ ] **Step 6: Commit**

```bash
git add Sources/VoiceFlowCore/WhisperEngine.swift Tests/VoiceFlowCoreTests/WhisperEngineTests.swift
git commit -m "feat: add WhisperEngine wrapper around whisper.cpp"
```

---

## Task 3: AudioCapture — mic capture and 16kHz mono resampling

**Files:**
- Create: `Sources/VoiceFlowCore/AudioCapture.swift`
- Test: `Tests/VoiceFlowCoreTests/AudioCaptureTests.swift`

**Interfaces:**
- Produces: `AudioCapture()`, `func start() throws`, `func stop() -> [Float]`, `isCapturing: Bool`. Task 5 and Task 10 call `start()` on hotkey press and `stop()` on hotkey release, feeding the result into `WhisperEngine.transcribe(samples:language:)`.

- [ ] **Step 1: Write the failing test for the pure resampling logic**

Live microphone capture can't run in a unit test (no mic in CI, no user present), so this test targets the resampling function in isolation using a synthetic buffer — no `AVAudioEngine` involved.

```swift
import Testing
import AVFoundation
@testable import VoiceFlowCore

@Suite struct AudioCaptureTests {
    @Test func resampleDownsamples44100To16000() throws {
        let inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
        let targetFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!

        let frameCount: AVAudioFrameCount = 44100 // 1 second of audio
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: frameCount)!
        buffer.frameLength = frameCount
        for i in 0..<Int(frameCount) {
            buffer.floatChannelData![0][i] = sin(Float(i) * 0.01)
        }

        let output = AudioCapture.resample(buffer: buffer, from: inputFormat, to: targetFormat)

        // ~1 second of audio at 16kHz should be close to 16000 samples.
        #expect(output.count > 15000)
        #expect(output.count < 17000)
    }

    @Test func resampleOfEmptyBufferIsEmpty() throws {
        let inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
        let targetFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: 0)!
        buffer.frameLength = 0

        let output = AudioCapture.resample(buffer: buffer, from: inputFormat, to: targetFormat)

        #expect(output.count == 0)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./scripts/test.sh --filter AudioCaptureTests`
Expected: FAIL — `AudioCapture` does not exist yet.

- [ ] **Step 3: Write the implementation**

`Sources/VoiceFlowCore/AudioCapture.swift`:

```swift
import AVFoundation

public final class AudioCapture {
    public private(set) var isCapturing = false

    private let engine = AVAudioEngine()
    private var samples: [Float] = []
    private let targetFormat = AVAudioFormat(
        commonFormat: .pcmFormatFloat32,
        sampleRate: 16000,
        channels: 1,
        interleaved: false
    )!

    public init() {}

    public func start() throws {
        guard !isCapturing else { return }
        samples.removeAll()

        let inputNode = engine.inputNode
        let inputFormat = inputNode.inputFormat(forBus: 0)

        inputNode.installTap(onBus: 0, bufferSize: 1024, format: inputFormat) { [weak self] buffer, _ in
            guard let self else { return }
            let converted = Self.resample(buffer: buffer, from: inputFormat, to: self.targetFormat)
            self.samples.append(contentsOf: converted)
        }

        engine.prepare()
        try engine.start()
        isCapturing = true
    }

    public func stop() -> [Float] {
        guard isCapturing else { return samples }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        isCapturing = false
        return samples
    }

    static func resample(buffer: AVAudioPCMBuffer, from inputFormat: AVAudioFormat, to targetFormat: AVAudioFormat) -> [Float] {
        guard buffer.frameLength > 0 else { return [] }
        guard let converter = AVAudioConverter(from: inputFormat, to: targetFormat) else { return [] }

        let ratio = targetFormat.sampleRate / inputFormat.sampleRate
        let outputCapacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 16
        guard let outputBuffer = AVAudioPCMBuffer(pcmFormat: targetFormat, frameCapacity: outputCapacity) else { return [] }

        var error: NSError?
        var didProvideInput = false
        converter.convert(to: outputBuffer, error: &error) { _, outStatus in
            if didProvideInput {
                outStatus.pointee = .noDataNow
                return nil
            }
            didProvideInput = true
            outStatus.pointee = .haveData
            return buffer
        }

        guard error == nil, let channelData = outputBuffer.floatChannelData else { return [] }
        let frameLength = Int(outputBuffer.frameLength)
        return Array(UnsafeBufferPointer(start: channelData[0], count: frameLength))
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./scripts/test.sh --filter AudioCaptureTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Manual verification note**

Real microphone capture (`start()`/`stop()` against `AVAudioEngine.inputNode`) is verified in Task 5's spike, where you can actually speak into the mic and inspect the captured sample count/duration. Add `NSMicrophoneUsageDescription` to `Info.plist` when Task 10 creates it — without it, `engine.start()` throws on first mic access.

- [ ] **Step 6: Commit**

```bash
git add Sources/VoiceFlowCore/AudioCapture.swift Tests/VoiceFlowCoreTests/AudioCaptureTests.swift
git commit -m "feat: add AudioCapture with 16kHz mono resampling"
```

---

## Task 4: HotkeyManager — global push-to-talk hotkey

**Files:**
- Create: `Sources/VoiceFlowCore/HotkeyManager.swift`

**Interfaces:**
- Consumes: `HotKey` product (soffes/HotKey, added in Task 1).
- Produces: `HotkeyManager()`, `func register(key: Key, modifiers: NSEvent.ModifierFlags) -> Bool`, `func unregister()`, `onPress: (() -> Void)?`, `onRelease: (() -> Void)?`. Task 5 and Task 10 use `onPress`/`onRelease` to drive `AudioCapture.start()`/`stop()`.

No automated test: a global hotkey only fires from real OS-level key events, which don't exist in a test runner. This task is verified manually, and its correctness is folded into Task 5's spike run (if the hotkey didn't work, the spike wouldn't produce any audio to transcribe).

- [ ] **Step 1: Write the implementation**

`Sources/VoiceFlowCore/HotkeyManager.swift`:

```swift
import AppKit
import HotKey

public final class HotkeyManager {
    public var onPress: (() -> Void)?
    public var onRelease: (() -> Void)?

    private var hotKey: HotKey?

    public init() {}

    /// Registers the global hotkey. Returns `false` if registration failed
    /// (e.g. another app already owns this combination) — the caller is
    /// responsible for surfacing that to the user (see Task 12).
    @discardableResult
    public func register(key: Key = .space, modifiers: NSEvent.ModifierFlags = [.control, .option]) -> Bool {
        let newHotKey = HotKey(key: key, modifiers: modifiers)
        newHotKey.keyDownHandler = { [weak self] in self?.onPress?() }
        newHotKey.keyUpHandler = { [weak self] in self?.onRelease?() }
        self.hotKey = newHotKey
        return newHotKey.isRegistered
    }

    public func unregister() {
        hotKey = nil
    }
}
```

> Verify `HotKey.isRegistered` still exists on whatever HotKey version `swift build` resolves in Task 1 — check `.build/checkouts/HotKey/Sources/HotKey/HotKey.swift`. If the API differs, adapt the return value accordingly; the default hotkey stays `Control+Option+Space` either way, matching the spec.

- [ ] **Step 2: Build to confirm it compiles**

Run: `swift build`
Expected: succeeds.

- [ ] **Step 3: Manual verification**

Add a temporary block to `LatencySpike/main.swift`:

```swift
let hotkey = HotkeyManager()
let registered = hotkey.register()
print("Hotkey registered: \(registered)")
hotkey.onPress = { print("PRESS") }
hotkey.onRelease = { print("RELEASE") }
RunLoop.main.run()
```

Run: `swift run LatencySpike`, then press and release Control+Option+Space.
Expected: console prints `Hotkey registered: true`, then `PRESS` and `RELEASE` on each press/release. Ctrl+C to stop. Remove this temporary block before Task 5 (which replaces it with the real spike).

- [ ] **Step 4: Commit**

```bash
git add Sources/VoiceFlowCore/HotkeyManager.swift
git commit -m "feat: add HotkeyManager wrapping the HotKey package"
```

---

## Task 5: Latency spike — the risk-validation milestone

This is the milestone the spec calls out explicitly: measure whisper.cpp CPU latency *before* building the rest of the app around an unverified assumption. Do not proceed to Task 6 until this task's manual measurement step is done and recorded.

**Files:**
- Modify: `Sources/LatencySpike/main.swift`

**Interfaces:**
- Consumes: `HotkeyManager` (Task 4), `AudioCapture` (Task 3), `WhisperEngine` (Task 2).
- Produces: console output with press/release/inference timestamps and total latency — no other task depends on this file's contents, it is a standalone diagnostic.

- [ ] **Step 1: Write the spike**

`Sources/LatencySpike/main.swift`:

```swift
import Foundation
import AppKit
import VoiceFlowCore

// Point this at a real model you've downloaded manually, e.g.:
// curl -L -o /tmp/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
guard CommandLine.arguments.count > 1 else {
    print("Usage: swift run LatencySpike <path-to-ggml-model.bin>")
    exit(1)
}
let modelPath = CommandLine.arguments[1]

print("Loading model at \(modelPath)...")
let loadStart = Date()
let engine = try WhisperEngine(modelPath: modelPath)
print("Model loaded in \(Date().timeIntervalSince(loadStart))s")

let audioCapture = AudioCapture()
let hotkey = HotkeyManager()

guard hotkey.register() else {
    print("Failed to register hotkey — is another app using Control+Option+Space?")
    exit(1)
}

var pressTime: Date?

hotkey.onPress = {
    pressTime = Date()
    do {
        try audioCapture.start()
        print("\n[listening...] speak now, release the hotkey when done")
    } catch {
        print("Failed to start audio capture: \(error)")
    }
}

hotkey.onRelease = {
    guard let pressTime else { return }
    let releaseTime = Date()
    let samples = audioCapture.stop()
    print("Captured \(samples.count) samples (\(Double(samples.count) / 16000.0)s of audio) in \(releaseTime.timeIntervalSince(pressTime))s")

    do {
        let inferenceStart = Date()
        let result = try engine.transcribe(samples: samples, language: "it")
        let totalLatency = Date().timeIntervalSince(releaseTime)
        print("Transcription: \"\(result.text)\"")
        print("Inference time: \(result.durationSeconds)s")
        print("Total latency from hotkey release to text ready: \(totalLatency)s")
    } catch {
        print("Transcription failed: \(error)")
    }
}

print("Ready. Hold Control+Option+Space, speak, then release.")
RunLoop.main.run()
```

- [ ] **Step 2: Run the spike with the `base` model**

```bash
curl -L -o /tmp/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
swift run LatencySpike /tmp/ggml-base.bin
```

Speak a short sentence (5-10 seconds) in Italian, then in English, holding the hotkey each time. Record the printed "Total latency" for each run.

- [ ] **Step 3: Run the spike with the `small` model**

```bash
curl -L -o /tmp/ggml-small.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin
swift run LatencySpike /tmp/ggml-small.bin
```

Repeat the same manual test. Record the printed "Total latency" for each run.

- [ ] **Step 4: Decision gate — record the measured numbers and pick the default model**

Write the actual measured latencies (not estimates) into `docs/superpowers/specs/2026-09-15-voice-dictation-mac-design.md` under a new "Latenza misurata" note, or into a dated note in `docs/`, per the "never invent data" rule — these are real numbers from Step 2/3, not placeholders. Based on those numbers, decide: does `base` ship as the v1 default with `small` optional (per spec's stated fallback), or is `small` fast enough to default to? This decision feeds the `WhisperModelName` default in Task 7.

- [ ] **Step 5: Commit**

```bash
git add Sources/LatencySpike/main.swift docs/
git commit -m "feat: build hotkey-to-transcript latency spike"
```

---

## Task 6: ModelManager — model storage, checksum, and download

**Files:**
- Create: `Sources/VoiceFlowCore/ModelManager.swift`
- Test: `Tests/VoiceFlowCoreTests/ModelManagerTests.swift`

**Interfaces:**
- Produces: `ModelInfo { name, fileName, downloadURL, sha256 }`, `ModelManager(modelsDirectory:)`, `func localPath(for:) -> URL`, `func isModelPresentAndValid(_:) -> Bool`, `static func sha256(ofFileAt:) throws -> String`, `func download(_:progress:completion:)`, `ModelManagerError`. Task 10 (app) calls `isModelPresentAndValid` on launch and `download` when it's `false`.

- [ ] **Step 1: Write the failing tests for path resolution and checksum verification**

```swift
import Foundation
import Testing
@testable import VoiceFlowCore

@Suite final class ModelManagerTests {
    let tempDir: URL
    let manager: ModelManager

    // Swift Testing creates a fresh suite instance per test, so init/deinit
    // are the per-test setup and teardown.
    init() {
        tempDir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try? FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        manager = ModelManager(modelsDirectory: tempDir)
    }

    deinit {
        try? FileManager.default.removeItem(at: tempDir)
    }

    @Test func localPathAppendsFileNameToModelsDirectory() {
        let model = ModelInfo(name: "base", fileName: "ggml-base.bin", downloadURL: URL(string: "https://example.com/ggml-base.bin")!, sha256: "irrelevant-for-this-test")
        #expect(manager.localPath(for: model) == tempDir.appendingPathComponent("ggml-base.bin"))
    }

    @Test func isModelPresentAndValidIsFalseWhenFileMissing() {
        let model = ModelInfo(name: "base", fileName: "missing.bin", downloadURL: URL(string: "https://example.com/missing.bin")!, sha256: "does-not-matter")
        #expect(!manager.isModelPresentAndValid(model))
    }

    @Test func isModelPresentAndValidIsFalseWhenChecksumMismatches() throws {
        let fileURL = tempDir.appendingPathComponent("corrupt.bin")
        try "not the real model".write(to: fileURL, atomically: true, encoding: .utf8)
        let model = ModelInfo(name: "base", fileName: "corrupt.bin", downloadURL: URL(string: "https://example.com/corrupt.bin")!, sha256: "0000000000000000000000000000000000000000000000000000000000000000")
        #expect(!manager.isModelPresentAndValid(model))
    }

    @Test func isModelPresentAndValidIsTrueWhenChecksumMatches() throws {
        let fileURL = tempDir.appendingPathComponent("fixture.bin")
        try "voiceflow-test-fixture\n".write(to: fileURL, atomically: true, encoding: .utf8)
        // Real SHA256 of the literal bytes "voiceflow-test-fixture\n", computed with:
        // printf 'voiceflow-test-fixture\n' | shasum -a 256
        let model = ModelInfo(name: "fixture", fileName: "fixture.bin", downloadURL: URL(string: "https://example.com/fixture.bin")!, sha256: "f8a7289ca2e97501bf779dfc71be00dda2b3e4bdecc94a0eef00451e643e04b3")
        #expect(manager.isModelPresentAndValid(model))
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./scripts/test.sh --filter ModelManagerTests`
Expected: FAIL — `ModelManager` does not exist yet.

- [ ] **Step 3: Write the implementation**

`Sources/VoiceFlowCore/ModelManager.swift`:

```swift
import Foundation
import CryptoKit

public struct ModelInfo {
    public let name: String
    public let fileName: String
    public let downloadURL: URL
    public let sha256: String

    public init(name: String, fileName: String, downloadURL: URL, sha256: String) {
        self.name = name
        self.fileName = fileName
        self.downloadURL = downloadURL
        self.sha256 = sha256
    }
}

public enum ModelManagerError: Error {
    case checksumMismatch(expected: String, actual: String)
    case downloadFailed(underlying: Error)
}

public final class ModelManager {
    // NOTE: sha256 values below are placeholders and MUST be replaced with the
    // real, verified SHA256 of each file before this ships — compute them with
    // `curl -L <url> -o /tmp/model.bin && shasum -a 256 /tmp/model.bin` against
    // the actual file from huggingface.co/ggerganov/whisper.cpp. Never guess
    // this value — an unverified hash silently defeats corruption detection.
    public static let knownModels: [ModelInfo] = [
        ModelInfo(
            name: "base",
            fileName: "ggml-base.bin",
            downloadURL: URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin")!,
            sha256: "VERIFY_AND_REPLACE_BEFORE_SHIPPING"
        ),
        ModelInfo(
            name: "small",
            fileName: "ggml-small.bin",
            downloadURL: URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin")!,
            sha256: "VERIFY_AND_REPLACE_BEFORE_SHIPPING"
        )
    ]

    private let modelsDirectory: URL

    public init(modelsDirectory: URL = ModelManager.defaultModelsDirectory()) {
        self.modelsDirectory = modelsDirectory
    }

    public static func defaultModelsDirectory() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("VoiceFlow/models", isDirectory: true)
    }

    public func localPath(for model: ModelInfo) -> URL {
        modelsDirectory.appendingPathComponent(model.fileName)
    }

    public func isModelPresentAndValid(_ model: ModelInfo) -> Bool {
        let path = localPath(for: model)
        guard FileManager.default.fileExists(atPath: path.path) else { return false }
        guard let actual = try? Self.sha256(ofFileAt: path) else { return false }
        return actual == model.sha256
    }

    public static func sha256(ofFileAt url: URL) throws -> String {
        let data = try Data(contentsOf: url)
        let digest = SHA256.hash(data: data)
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    public func download(
        _ model: ModelInfo,
        progress: @escaping (Double) -> Void,
        completion: @escaping (Result<URL, ModelManagerError>) -> Void
    ) {
        try? FileManager.default.createDirectory(at: modelsDirectory, withIntermediateDirectories: true)
        let destination = localPath(for: model)

        let task = URLSession.shared.downloadTask(with: model.downloadURL) { tempURL, _, error in
            if let error {
                completion(.failure(.downloadFailed(underlying: error)))
                return
            }
            guard let tempURL else {
                completion(.failure(.downloadFailed(underlying: URLError(.badServerResponse))))
                return
            }
            do {
                if FileManager.default.fileExists(atPath: destination.path) {
                    try FileManager.default.removeItem(at: destination)
                }
                try FileManager.default.moveItem(at: tempURL, to: destination)
                let actual = try Self.sha256(ofFileAt: destination)
                guard actual == model.sha256 else {
                    try? FileManager.default.removeItem(at: destination)
                    completion(.failure(.checksumMismatch(expected: model.sha256, actual: actual)))
                    return
                }
                completion(.success(destination))
            } catch {
                completion(.failure(.downloadFailed(underlying: error)))
            }
        }

        let observation = task.progress.observe(\.fractionCompleted) { fractionCompleted, _ in
            progress(fractionCompleted.fractionCompleted)
        }
        objc_setAssociatedObject(task, &Self.progressObservationKey, observation, .OBJC_ASSOCIATION_RETAIN)
        task.resume()
    }

    private static var progressObservationKey: UInt8 = 0
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `./scripts/test.sh --filter ModelManagerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Replace the placeholder checksums with real ones**

```bash
curl -L -o /tmp/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
shasum -a 256 /tmp/ggml-base.bin
curl -L -o /tmp/ggml-small.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin
shasum -a 256 /tmp/ggml-small.bin
```

Paste the two real hashes into `ModelManager.knownModels` in place of `VERIFY_AND_REPLACE_BEFORE_SHIPPING`. Do not proceed to Task 10 with placeholders still in place — the app's corruption-detection error path (spec: "Modello mancante o corrotto") is a no-op until this is done.

- [ ] **Step 6: Manual verification of a real download**

Temporarily call `ModelManager().download(ModelManager.knownModels[0], progress: { print($0) }, completion: { print($0) })` from `LatencySpike/main.swift`, run it, confirm the progress callback fires with increasing values and the file lands in `~/Library/Application Support/VoiceFlow/models/ggml-base.bin`. Remove the temporary call afterward.

> **Ruling R6:** insert this temporary call *before* `RunLoop.main.run()` in the spike's `main.swift`. After Task 5 that file ends in `RunLoop.main.run()`, which never returns — anything appended after it would never execute, so the check would silently prove nothing.

- [ ] **Step 7: Commit**

```bash
git add Sources/VoiceFlowCore/ModelManager.swift Tests/VoiceFlowCoreTests/ModelManagerTests.swift
git commit -m "feat: add ModelManager for model storage, checksum, and download"
```

---

## Task 7: SettingsStore — user preferences

**Files:**
- Create: `Sources/VoiceFlowCore/SettingsStore.swift`
- Test: `Tests/VoiceFlowCoreTests/SettingsStoreTests.swift`

**Interfaces:**
- Produces: `WhisperModelName` (`.base`/`.small`), `DictationLanguage` (`.italian`/`.english`), `SettingsStore(defaults:)`, `var model: WhisperModelName`, `var language: DictationLanguage`, `var launchAtLogin: Bool`. Task 10 reads `model`/`language` to pick which `ModelInfo` to load and which language code to pass to `WhisperEngine.transcribe`; Task 11's settings UI binds directly to these three properties.

- [ ] **Step 1: Write the failing tests**

```swift
import Foundation
import Testing
@testable import VoiceFlowCore

// .serialized because every test in this suite shares one UserDefaults
// domain — running them concurrently would let one test's reset wipe
// another's writes mid-assertion.
@Suite(.serialized) final class SettingsStoreTests {
    private static let suiteName = "com.voiceflow.tests.settings"
    let defaults: UserDefaults
    let store: SettingsStore

    init() {
        defaults = UserDefaults(suiteName: Self.suiteName)!
        defaults.removePersistentDomain(forName: Self.suiteName)
        store = SettingsStore(defaults: defaults)
    }

    deinit {
        defaults.removePersistentDomain(forName: Self.suiteName)
    }

    @Test func defaultModelIsBase() {
        #expect(store.model == .base)
    }

    @Test func defaultLanguageIsItalian() {
        #expect(store.language == .italian)
    }

    @Test func defaultLaunchAtLoginIsFalse() {
        #expect(!store.launchAtLogin)
    }

    @Test func modelRoundTrips() {
        store.model = .small
        #expect(store.model == .small)
    }

    @Test func languageRoundTrips() {
        store.language = .english
        #expect(store.language == .english)
    }

    @Test func launchAtLoginRoundTrips() {
        store.launchAtLogin = true
        #expect(store.launchAtLogin)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `./scripts/test.sh --filter SettingsStoreTests`
Expected: FAIL — `SettingsStore` does not exist yet.

- [ ] **Step 3: Write the implementation**

`Sources/VoiceFlowCore/SettingsStore.swift`:

```swift
import Foundation

public enum WhisperModelName: String, CaseIterable, Codable {
    case base
    case small
}

public enum DictationLanguage: String, CaseIterable, Codable {
    case italian = "it"
    case english = "en"
}

public final class SettingsStore {
    private enum Key {
        static let model = "voiceflow.model"
        static let language = "voiceflow.language"
        static let launchAtLogin = "voiceflow.launchAtLogin"
    }

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    public var model: WhisperModelName {
        get { WhisperModelName(rawValue: defaults.string(forKey: Key.model) ?? "") ?? .base }
        set { defaults.set(newValue.rawValue, forKey: Key.model) }
    }

    public var language: DictationLanguage {
        get { DictationLanguage(rawValue: defaults.string(forKey: Key.language) ?? "") ?? .italian }
        set { defaults.set(newValue.rawValue, forKey: Key.language) }
    }

    public var launchAtLogin: Bool {
        get { defaults.bool(forKey: Key.launchAtLogin) }
        set { defaults.set(newValue, forKey: Key.launchAtLogin) }
    }
}
```

> If Task 5's latency measurement showed `small` is fast enough to default to, change `?? .base` to `?? .small` here and note why in a comment referencing the measured number.

- [ ] **Step 4: Run tests to verify they pass**

Run: `./scripts/test.sh --filter SettingsStoreTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add Sources/VoiceFlowCore/SettingsStore.swift Tests/VoiceFlowCoreTests/SettingsStoreTests.swift
git commit -m "feat: add SettingsStore for model, language, and launch-at-login prefs"
```

---

## Task 8: PermissionsManager — microphone and Accessibility status

**Files:**
- Create: `Sources/VoiceFlowCore/PermissionsManager.swift`

**Interfaces:**
- Produces: `PermissionStatus` (`.granted`/`.denied`/`.notDetermined`), `PermissionsManager()`, `func microphoneStatus() -> PermissionStatus`, `func requestMicrophoneAccess(completion:)`, `func accessibilityStatus() -> PermissionStatus`, `func promptAccessibilityAccess()`. Task 10's status bar controller polls `microphoneStatus()`/`accessibilityStatus()` on launch and before each dictation to decide whether to show the "permesso mancante" state from the spec's error-handling section.

No automated test: every method here is a thin pass-through to a real macOS permission API (`AVCaptureDevice`, `AXIsProcessTrusted`) that behaves differently depending on what the user has actually granted — there is no meaningful way to unit test it without mocking the OS itself, which would test the mock, not the app. Verified manually in Task 12.

- [ ] **Step 1: Write the implementation**

`Sources/VoiceFlowCore/PermissionsManager.swift`:

```swift
import AVFoundation
import ApplicationServices

public enum PermissionStatus {
    case granted
    case denied
    case notDetermined
}

public final class PermissionsManager {
    public init() {}

    public func microphoneStatus() -> PermissionStatus {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized: return .granted
        case .denied, .restricted: return .denied
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    public func requestMicrophoneAccess(completion: @escaping (Bool) -> Void) {
        AVCaptureDevice.requestAccess(for: .audio, completionHandler: completion)
    }

    public func accessibilityStatus() -> PermissionStatus {
        AXIsProcessTrusted() ? .granted : .denied
    }

    /// Shows the system's own "VoiceFlow wants to control this computer" prompt,
    /// which deep-links to System Settings > Privacy & Security > Accessibility.
    public func promptAccessibilityAccess() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `swift build`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add Sources/VoiceFlowCore/PermissionsManager.swift
git commit -m "feat: add PermissionsManager for microphone and Accessibility status"
```

---

## Task 9: TextInjector — writing text into the focused app

**Files:**
- Create: `Sources/VoiceFlowCore/TextInjector.swift`

**Interfaces:**
- Consumes: `PermissionsManager.accessibilityStatus()` semantics (Task 8) — caller is expected to check Accessibility is granted before calling.
- Produces: `TextInjector()`, `func inject(_ text: String) throws`, `TextInjectionError`. Task 10 calls `inject(_:)` with the transcription result from `WhisperEngine`.

No automated test: injecting text requires a real focused text field in a real running app — there's nothing to assert against in a test process with no UI focus. Verified manually per the spec's own verification plan (dictate into TextEdit and one other app).

- [ ] **Step 1: Write the implementation**

`Sources/VoiceFlowCore/TextInjector.swift`:

```swift
import ApplicationServices
import CoreGraphics

public enum TextInjectionError: Error {
    case accessibilityNotTrusted
}

public final class TextInjector {
    public init() {}

    public func inject(_ text: String) throws {
        guard AXIsProcessTrusted() else {
            throw TextInjectionError.accessibilityNotTrusted
        }
        if insertViaAccessibility(text) {
            return
        }
        insertViaSyntheticKeystrokes(text)
    }

    /// Tries to write directly into the focused element's selected-text
    /// attribute. Works well for standard text fields; returns `false` for
    /// apps whose accessibility tree doesn't support this attribute, so the
    /// caller falls back to synthetic keystrokes.
    private func insertViaAccessibility(_ text: String) -> Bool {
        let systemWide = AXUIElementCreateSystemWide()
        var focusedElementRef: AnyObject?
        let copyResult = AXUIElementCopyAttributeValue(
            systemWide,
            kAXFocusedUIElementAttribute as CFString,
            &focusedElementRef
        )
        guard copyResult == .success, let focusedElementRef else { return false }
        let focusedElement = focusedElementRef as! AXUIElement

        let setResult = AXUIElementSetAttributeValue(
            focusedElement,
            kAXSelectedTextAttribute as CFString,
            text as CFTypeRef
        )
        return setResult == .success
    }

    /// Fallback: synthesizes keyboard events carrying the Unicode text
    /// directly, bypassing the need for the target app to expose a
    /// settable accessibility attribute.
    private func insertViaSyntheticKeystrokes(_ text: String) {
        let source = CGEventSource(stateID: .hidSystemState)
        for scalar in text.unicodeScalars {
            let utf16 = Array(String(scalar).utf16)
            guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: true) else { continue }
            keyDown.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyDown.post(tap: .cghidEventTap)

            guard let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: false) else { continue }
            keyUp.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyUp.post(tap: .cghidEventTap)
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `swift build`
Expected: succeeds.

- [ ] **Step 3: Manual verification**

This needs a running app with a real focused field, which the `LatencySpike` command-line tool doesn't have — defer the actual "does text appear in TextEdit" check to Task 10's manual verification, once there's a full app to grant Accessibility permission to. Note that here so it isn't silently skipped.

- [ ] **Step 4: Commit**

```bash
git add Sources/VoiceFlowCore/TextInjector.swift
git commit -m "feat: add TextInjector with Accessibility API and synthetic-keystroke fallback"
```

---

## Task 10: VoiceFlowApp executable target — menu bar shell, end-to-end wiring, and the `.app` bundle

This is where `VoiceFlowCore` stops being a library nobody runs and becomes the actual app. Per the ruling recorded at the top of this plan (this machine has no Xcode.app, and a subagent has no GUI to drive Xcode's project wizard anyway), `VoiceFlowApp` is a plain SPM `executableTarget` — same target name and same wiring the original Xcode-based design called for, just reached without a `.xcodeproj`. A packaging script turns the built binary into a real, launchable `VoiceFlow.app`.

**Files:**
- Modify: `Package.swift` (add the `VoiceFlowApp` executable target)
- Create: `Sources/VoiceFlowApp/VoiceFlowApp.swift`
- Create: `Sources/VoiceFlowApp/StatusBarController.swift`
- Create: `Resources/VoiceFlowApp/Info.plist`
- Create: `scripts/package-app.sh`

**Interfaces:**
- Consumes: `HotkeyManager`, `AudioCapture`, `WhisperEngine`, `ModelManager`, `SettingsStore`, `PermissionsManager`, `TextInjector` (Tasks 2-9).
- Produces: `DictationState` enum and `StatusBarController` that Task 11 (settings UI) and Task 12 (error handling) both extend; `dist/VoiceFlow.app`, the actual double-clickable app.

- [ ] **Step 1: Add the `VoiceFlowApp` executable target to `Package.swift`**

Add to the `products` array:

```swift
.executable(name: "VoiceFlowApp", targets: ["VoiceFlowApp"])
```

Add to the `targets` array, alongside `LatencySpike`:

```swift
.executableTarget(
    name: "VoiceFlowApp",
    dependencies: ["VoiceFlowCore"]
)
```

- [ ] **Step 2: Write `Info.plist` for a menu-bar-only app**

`Resources/VoiceFlowApp/Info.plist` (this is not inside an `.xcodeproj` — the packaging script in Step 6 copies it into the `.app` bundle it assembles):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>VoiceFlow</string>
    <key>CFBundleIdentifier</key>
    <string>com.voiceflow.app</string>
    <key>CFBundleName</key>
    <string>VoiceFlow</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSMicrophoneUsageDescription</key>
    <string>VoiceFlow needs microphone access to transcribe your speech locally on this Mac. Audio is never sent over the network.</string>
</dict>
</plist>
```

> `LSUIElement` (no Dock icon) and `NSMicrophoneUsageDescription` (required or `AVCaptureDevice.requestAccess` crashes the process) only take effect when the app is launched as a real bundle with this `Info.plist` inside it — never test permission/Dock behavior by running the raw binary via `swift run VoiceFlowApp`. Always test through `dist/VoiceFlow.app` from Step 6 onward.

- [ ] **Step 3: Write the app entry point**

`Sources/VoiceFlowApp/VoiceFlowApp.swift`:

```swift
import SwiftUI
import VoiceFlowCore

@main
struct VoiceFlowApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        Settings {
            EmptyView()
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var statusBarController: StatusBarController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusBarController = StatusBarController()
    }
}
```

- [ ] **Step 4: Write `StatusBarController` with the full push-to-talk wiring**

```swift
import AppKit
import VoiceFlowCore

enum DictationState {
    case idle
    case listening
    case transcribing
    case error(String)
}

final class StatusBarController {
    private let statusItem: NSStatusItem
    private let hotkeyManager = HotkeyManager()
    private let audioCapture = AudioCapture()
    private let permissionsManager = PermissionsManager()
    private let textInjector = TextInjector()
    private let settingsStore = SettingsStore()
    private let modelManager = ModelManager()
    private var whisperEngine: WhisperEngine?

    private var state: DictationState = .idle {
        didSet { updateIcon() }
    }

    init() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        updateIcon()
        requestMicrophoneAccessIfNeeded()
        loadModelIfPresent()
        registerHotkey()
    }

    /// Ruling R7: ask for microphone access at launch, not mid-dictation.
    /// On a fresh install the status is `.notDetermined`; asking while the
    /// user holds the push-to-talk hotkey would pop the system dialog, and by
    /// the time they answered it they'd have released the key — leaving the
    /// state machine stuck in `.listening` with no release event coming.
    private func requestMicrophoneAccessIfNeeded() {
        guard permissionsManager.microphoneStatus() == .notDetermined else { return }
        permissionsManager.requestMicrophoneAccess { granted in
            DispatchQueue.main.async { [weak self] in
                if !granted {
                    self?.state = .error("microphone-permission")
                }
            }
        }
    }

    private func updateIcon() {
        let symbolName: String
        switch state {
        case .idle: symbolName = "mic"
        case .listening: symbolName = "mic.fill"
        case .transcribing: symbolName = "waveform"
        case .error: symbolName = "exclamationmark.triangle"
        }
        statusItem.button?.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: "VoiceFlow status")
    }

    private func loadModelIfPresent() {
        let model = ModelManager.knownModels.first { $0.name == settingsStore.model.rawValue }!
        guard modelManager.isModelPresentAndValid(model) else {
            state = .error("model-missing")
            return
        }
        do {
            whisperEngine = try WhisperEngine(modelPath: modelManager.localPath(for: model).path)
        } catch {
            state = .error("model-load-failed")
        }
    }

    private func registerHotkey() {
        hotkeyManager.onPress = { [weak self] in self?.beginDictation() }
        hotkeyManager.onRelease = { [weak self] in self?.finishDictation() }
        _ = hotkeyManager.register()
    }

    private func beginDictation() {
        guard permissionsManager.microphoneStatus() == .granted else {
            state = .error("microphone-permission")
            return
        }
        do {
            try audioCapture.start()
            state = .listening
        } catch {
            state = .error("audio-start-failed")
        }
    }

    private func finishDictation() {
        guard case .listening = state else { return }
        state = .transcribing
        let samples = audioCapture.stop()

        guard let whisperEngine else {
            state = .error("model-missing")
            return
        }
        guard permissionsManager.accessibilityStatus() == .granted else {
            state = .error("accessibility-permission")
            return
        }

        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self else { return }
            do {
                let result = try whisperEngine.transcribe(samples: samples, language: self.settingsStore.language.rawValue)
                // Injection drives the Accessibility API and posts CGEvents,
                // both of which belong on the main thread.
                DispatchQueue.main.async {
                    do {
                        try self.textInjector.inject(result.text)
                        self.state = .idle
                    } catch {
                        self.state = .error("accessibility-permission")
                    }
                }
            } catch {
                DispatchQueue.main.async { self.state = .error("transcription-failed") }
            }
        }
    }
}
```

- [ ] **Step 5: Write the packaging script**

`scripts/package-app.sh` (this is the file that produces the actual `.app` executable to test on this Mac):

```bash
#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

echo "Building VoiceFlowApp (release)..."
swift build -c release --product VoiceFlowApp

APP_DIR="dist/VoiceFlow.app"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp .build/release/VoiceFlowApp "$APP_DIR/Contents/MacOS/VoiceFlow"
cp Resources/VoiceFlowApp/Info.plist "$APP_DIR/Contents/Info.plist"

echo "Ad-hoc signing (no Developer ID needed for local testing)..."
codesign --force --deep --sign - "$APP_DIR"

echo "Built $APP_DIR"
```

```bash
chmod +x scripts/package-app.sh
```

> Ad-hoc signing (`--sign -`) is sufficient to run on this same Mac and lets TCC (the microphone/Accessibility permission system) associate a stable identity with the app across rebuilds. It is not the same as Developer ID signing or notarization — both explicitly out of scope for v1 (spec: "Fuori scope").

- [ ] **Step 6: Build, package, and run**

Run: `swift build` (debug, fast feedback that the target compiles)
Expected: builds successfully.

Run: `./scripts/package-app.sh`
Expected: prints `Built dist/VoiceFlow.app`.

Run: `open dist/VoiceFlow.app`
Expected: no Dock icon appears; a microphone icon appears in the menu bar.

- [ ] **Step 7: Manual end-to-end verification**

- With a valid model already downloaded to `~/Library/Application Support/VoiceFlow/models/` (from Task 6's manual download step, renamed/placed to match `ModelManager.knownModels`), grant microphone and Accessibility permission when prompted.
- Open TextEdit, click into a document, hold Control+Option+Space, say a short sentence, release.
- Expected: the icon cycles idle → listening → transcribing → idle, and the spoken text appears at the cursor in TextEdit.
- Repeat in one more app (e.g. Notes or a code editor) per the spec's verification plan.

- [ ] **Step 8: Commit**

```bash
git add Package.swift Sources/VoiceFlowApp Resources/VoiceFlowApp scripts/package-app.sh
git commit -m "feat: add VoiceFlowApp menu bar shell wiring hotkey, audio, whisper, and text injection"
```

Note: `dist/` is a build output, not source — add it to `.gitignore` if it isn't already ignored, rather than committing the built `.app`.

---

## Task 11: Settings popover — model, language, hotkey, launch at login

**Files:**
- Create: `Sources/VoiceFlowApp/SettingsView.swift`
- Modify: `Sources/VoiceFlowApp/StatusBarController.swift`

**Interfaces:**
- Consumes: `SettingsStore` (Task 7), `HotkeyManager.register(key:modifiers:)` (Task 4).
- Produces: a popover attached to the status item's left-click, per spec: "impostazioni minime: modello, lingua, hotkey, avvio al login."

- [ ] **Step 1: Write the settings view**

`Sources/VoiceFlowApp/SettingsView.swift`:

```swift
import SwiftUI
import ServiceManagement
import VoiceFlowCore

struct SettingsPopoverView: View {
    @ObservedObject var viewModel: SettingsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("VoiceFlow").font(.headline)

            Picker("Model", selection: $viewModel.model) {
                Text("Base (faster)").tag(WhisperModelName.base)
                Text("Small (more accurate)").tag(WhisperModelName.small)
            }

            Picker("Language", selection: $viewModel.language) {
                Text("Italiano").tag(DictationLanguage.italian)
                Text("English").tag(DictationLanguage.english)
            }

            Toggle("Launch at login", isOn: $viewModel.launchAtLogin)

            Text("Hotkey: Control+Option+Space").foregroundStyle(.secondary)
        }
        .padding()
        .frame(width: 260)
    }
}

final class SettingsViewModel: ObservableObject {
    private let store: SettingsStore

    @Published var model: WhisperModelName {
        didSet { store.model = model }
    }
    @Published var language: DictationLanguage {
        didSet { store.language = language }
    }
    @Published var launchAtLogin: Bool {
        didSet {
            store.launchAtLogin = launchAtLogin
            do {
                if launchAtLogin {
                    try SMAppService.mainApp.register()
                } else {
                    try SMAppService.mainApp.unregister()
                }
            } catch {
                print("Failed to update launch-at-login: \(error)")
            }
        }
    }

    init(store: SettingsStore) {
        self.store = store
        self.model = store.model
        self.language = store.language
        self.launchAtLogin = store.launchAtLogin
    }
}
```

> Hotkey rebinding (choosing a different combination than Control+Option+Space) is explicitly not built here — v1's scope per the spec is a single configurable-in-code default with conflict detection (Task 12), not a UI key-recorder. If you want a rebind UI, confirm that's actually in scope before adding it — it isn't currently listed in `CLAUDE.md`'s "impostazioni minime."

- [ ] **Step 2: Wire the popover into `StatusBarController`**

Add to `StatusBarController.swift`:

```swift
import SwiftUI

// Add as a stored property:
private lazy var popover: NSPopover = {
    let popover = NSPopover()
    popover.contentSize = NSSize(width: 260, height: 180)
    popover.behavior = .transient
    popover.contentViewController = NSHostingController(rootView: SettingsPopoverView(viewModel: SettingsViewModel(store: settingsStore)))
    return popover
}()

// In init(), after updateIcon():
statusItem.button?.action = #selector(togglePopover)
statusItem.button?.target = self

@objc private func togglePopover() {
    guard let button = statusItem.button else { return }
    if popover.isShown {
        popover.performClose(nil)
    } else {
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
    }
}
```

> This makes a left-click on the status item open settings, which conflicts with the icon being purely a status indicator. That's an intentional v1 simplification (per spec: "apre un piccolo popover per le impostazioni" — no mention of a separate settings-only click target). Confirm this UX is acceptable before shipping; if not, the fix is a right-click / left-click split with `NSStatusBarButton`'s `sendAction(on:)`.

- [ ] **Step 3: Build, package, and run**

Run: `swift build`
Expected: builds successfully.

Run: `./scripts/package-app.sh && open dist/VoiceFlow.app`

- [ ] **Step 4: Manual verification**

Run the packaged app (never the raw `swift run` binary — see Task 10 Step 2's note on why), click the status item, confirm the popover shows model/language pickers and the launch-at-login toggle. Change the model, quit and relaunch the app, confirm the selection persisted (backed by `SettingsStore`'s `UserDefaults`, verified in Task 7). Toggle launch-at-login on, check System Settings > General > Login Items to confirm VoiceFlow is listed; toggle off and confirm it's removed.

> `SMAppService` registration can be pickier about ad-hoc-signed, non-`/Applications` app bundles than about a properly signed, installed one. If the toggle silently fails to register, note it as a known limitation of local ad-hoc testing rather than a code bug — confirm by checking `SMAppService.mainApp.status` right after calling `register()`.

- [ ] **Step 5: Commit**

```bash
git add Sources/VoiceFlowApp
git commit -m "feat: add settings popover for model, language, and launch at login"
```

---

## Task 12: Error handling — the spec's five failure modes

Every bullet in the spec's "Gestione errori" section gets its own explicit handling here; the goal is no silent failures.

**Files:**
- Modify: `Sources/VoiceFlowApp/StatusBarController.swift`

**Interfaces:**
- Consumes: `DictationState.error(String)` (Task 10), `PermissionsManager` (Task 8), `ModelManager` (Task 6), `HotkeyManager.register` return value (Task 4).

- [ ] **Step 1: Add an alert-presenting helper and map each error code to spec-required behavior**

Add to `StatusBarController.swift`:

Add this method to the `StatusBarController` class body:

```swift
import AppKit

extension StatusBarController {
    func presentAlert(for errorCode: String) {
        let alert = NSAlert()
        switch errorCode {
        case "microphone-permission":
            alert.messageText = "Microphone access needed"
            alert.informativeText = "VoiceFlow needs microphone access to transcribe your speech. Grant it in System Settings > Privacy & Security > Microphone."
            alert.addButton(withTitle: "Open System Settings")
            alert.addButton(withTitle: "Cancel")
            if alert.runModal() == .alertFirstButtonReturn {
                NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone")!)
            }

        case "accessibility-permission":
            alert.messageText = "Accessibility access needed"
            alert.informativeText = "VoiceFlow needs Accessibility access to type text into other apps. Grant it in System Settings > Privacy & Security > Accessibility."
            alert.addButton(withTitle: "Open System Settings")
            alert.addButton(withTitle: "Cancel")
            if alert.runModal() == .alertFirstButtonReturn {
                permissionsManager.promptAccessibilityAccess()
            }

        case "model-missing":
            alert.messageText = "Model not downloaded"
            alert.informativeText = "The speech model hasn't been downloaded yet, or failed a corruption check. Download it now?"
            alert.addButton(withTitle: "Download")
            alert.addButton(withTitle: "Cancel")
            if alert.runModal() == .alertFirstButtonReturn {
                startModelDownload()
            }

        case "transcription-failed":
            alert.messageText = "Transcription failed"
            alert.informativeText = "Something went wrong during transcription. Try again."
            alert.addButton(withTitle: "OK")
            alert.runModal()

        case "hotkey-conflict":
            alert.messageText = "Hotkey already in use"
            alert.informativeText = "Control+Option+Space is already registered by another app. Choose a different one in Settings."
            alert.addButton(withTitle: "OK")
            alert.runModal()

        default:
            alert.messageText = "Unexpected error"
            alert.informativeText = errorCode
            alert.addButton(withTitle: "OK")
            alert.runModal()
        }
    }
}
```

> Add this as a `presentAlert(for:)` method on `StatusBarController` itself (in the same `StatusBarController.swift` file modified by Task 10, not a new file) — same-file `private` access means it can reach `permissionsManager` and the `startModelDownload()` method added in Step 4 directly, no access-level changes needed.

- [ ] **Step 2: Trigger the alert from the state machine**

In `StatusBarController`'s `state` `didSet` (Task 10), add:

```swift
private var state: DictationState = .idle {
    didSet {
        updateIcon()
        if case .error(let code) = state {
            presentAlert(for: code)
        }
    }
}
```

- [ ] **Step 3: Implement proactive permission checks on launch (not just on failure)**

Add to `StatusBarController.init()`, after `registerHotkey()`:

```swift
if permissionsManager.accessibilityStatus() != .granted {
    state = .error("accessibility-permission")
}
```

This satisfies the spec's requirement to detect missing Accessibility proactively rather than waiting for injection to fail silently.

- [ ] **Step 4: Implement model download with progress bar**

Add `startModelDownload()`:

```swift
private func startModelDownload() {
    let model = ModelManager.knownModels.first { $0.name == settingsStore.model.rawValue }!
    let progressAlert = NSAlert()
    progressAlert.messageText = "Downloading \(model.name) model..."
    let progressBar = NSProgressIndicator(frame: NSRect(x: 0, y: 0, width: 250, height: 20))
    progressBar.style = .bar
    progressBar.minValue = 0
    progressBar.maxValue = 1
    progressAlert.accessoryView = progressBar

    modelManager.download(model, progress: { fraction in
        DispatchQueue.main.async { progressBar.doubleValue = fraction }
    }, completion: { [weak self] result in
        DispatchQueue.main.async {
            switch result {
            case .success:
                self?.loadModelIfPresent()
                self?.state = .idle
            case .failure(let error):
                self?.state = .error("model-download-failed: \(error)")
            }
        }
    })
    progressAlert.runModal()
}
```

- [ ] **Step 5: Implement hotkey conflict detection**

Change `registerHotkey()` (Task 10) to:

```swift
private func registerHotkey() {
    hotkeyManager.onPress = { [weak self] in self?.beginDictation() }
    hotkeyManager.onRelease = { [weak self] in self?.finishDictation() }
    if !hotkeyManager.register() {
        state = .error("hotkey-conflict")
    }
}
```

- [ ] **Step 6: Implement a transcription timeout**

Note the real constraint before implementing: whisper.cpp's `whisper_full` call is synchronous and blocking — there's no built-in mid-inference cancellation used here, so a "timeout" can only update the UI early, not stop the underlying computation. Document this honestly rather than implying true cancellation. Change `finishDictation()`'s background block (Task 10):

**Ruling R9:** the timeout flag is read and written from two different threads (the timer fires on the main queue, the worker runs on a background queue). A bare captured `var` there is a data race. Confine the flag to the main queue instead — every read and write of `timedOut` below happens in a main-queue block, and the worker asks the main queue for the verdict once inference returns.

```swift
// Add as a stored property on StatusBarController — only ever touched on the main queue:
private var dictationTimedOut = false

// In finishDictation(), replacing the background block from Task 10:
dictationTimedOut = false
let timeoutWorkItem = DispatchWorkItem { [weak self] in
    guard let self else { return }
    self.dictationTimedOut = true
    self.state = .error("transcription-timeout")
}
DispatchQueue.main.asyncAfter(deadline: .now() + 15, execute: timeoutWorkItem)

DispatchQueue.global(qos: .userInitiated).async { [weak self] in
    guard let self else { return }
    do {
        let result = try whisperEngine.transcribe(samples: samples, language: self.settingsStore.language.rawValue)
        DispatchQueue.main.async {
            timeoutWorkItem.cancel()
            // The UI already reported a timeout; drop the late result rather
            // than injecting text the user has stopped expecting.
            guard !self.dictationTimedOut else { return }
            do {
                try self.textInjector.inject(result.text)
                self.state = .idle
            } catch {
                self.state = .error("accessibility-permission")
            }
        }
    } catch {
        DispatchQueue.main.async {
            timeoutWorkItem.cancel()
            guard !self.dictationTimedOut else { return }
            self.state = .error("transcription-failed")
        }
    }
}
```

> Injecting text moved onto the main queue too: `TextInjector` drives the Accessibility API and posts `CGEvent`s, which belong on the main thread. An injection failure now maps to the Accessibility-permission alert rather than being swallowed as a generic transcription failure.

Add a case for `"transcription-timeout"` to the `presentAlert(for:)` switch from Step 1, reusing the `"transcription-failed"` copy.

Honest limitation to keep in the code as a comment: `whisper_full` is synchronous and is not aborted here, so the timeout changes what the UI reports, not what the CPU is still doing. Do not word it as if inference were cancelled.

- [ ] **Step 7: Build, package, and run**

Run: `swift build && ./scripts/package-app.sh && open dist/VoiceFlow.app`
Expected: builds successfully and the app launches.

- [ ] **Step 8: Manual verification of every failure mode**

- Revoke microphone permission (System Settings) → press hotkey → confirm the alert appears with a working "Open System Settings" button.
- Revoke Accessibility permission → relaunch the app → confirm the alert appears on launch, not just after a failed injection.
- Delete the model file from `~/Library/Application Support/VoiceFlow/models/` → relaunch → confirm "Download" alert appears and, when accepted, shows a moving progress bar and completes.
- Corrupt the model file (truncate it with `truncate -s 1000 ~/Library/Application\ Support/VoiceFlow/models/ggml-base.bin`) → relaunch → confirm the checksum mismatch is detected and the same download flow triggers.
- Run another app that already binds Control+Option+Space (or temporarily hardcode a colliding combination) → confirm the conflict alert appears instead of a silent no-op.

- [ ] **Step 9: Commit**

```bash
git add Sources/VoiceFlowApp
git commit -m "feat: add error handling for permissions, model, hotkey conflict, and timeout"
```

---

## Task 13: Full manual verification pass

**Files:** none — this task is the spec's own "Piano di verifica," executed end-to-end and its results recorded.

- [ ] **Step 1: Cross-app dictation check**

Dictate into TextEdit and at least one other app (Notes, or a code editor). Confirm the text lands correctly at the cursor in both.

- [ ] **Step 2: Offline check**

With a model already downloaded, turn off Wi-Fi entirely. Confirm dictation still works identically. This is the core privacy claim of the whole app — do not skip it.

- [ ] **Step 3: Resource usage check**

Open Activity Monitor. Record VoiceFlowApp's memory/CPU at rest (idle, no dictation in progress) and during a transcription. Confirm both stay reasonable on the hardware being tested — record the actual numbers observed, per the "never invent data" rule, rather than an assumed figure.

- [ ] **Step 4: Accessibility revocation check**

Revoke Accessibility access mid-session (System Settings, while the app is running) and confirm the next dictation attempt shows the proactive alert (Task 12, Step 3/8) rather than silently failing to inject text.

- [ ] **Step 5: Model deletion check**

Delete `~/Library/Application Support/VoiceFlow/models/` entirely, relaunch, and confirm the app proposes a download instead of crashing.

- [ ] **Step 6: Record results**

Add a short "Verifica manuale eseguita" note to the spec or a dated file under `docs/`, listing the date, the actual measured resource numbers from Step 3, and which checks passed. This is the record that the "Consegnabile" bar from `CLAUDE.md` ("funziona all'apertura... con le assunzioni dichiarate") has actually been met, not assumed.

- [ ] **Step 7: Commit**

```bash
git add docs/
git commit -m "docs: record v1 manual verification results"
```
