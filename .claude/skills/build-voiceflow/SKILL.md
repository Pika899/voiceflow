---
name: build-voiceflow
description: Use when building, testing, packaging, or running the VoiceFlow macOS menu bar app — including producing the double-clickable VoiceFlow.app bundle, running the XCTest suite, or running the whisper.cpp latency spike.
---

# Build VoiceFlow

This machine has only the Xcode **Command Line Tools**, not Xcode.app. There is no
`.xcodeproj` and `xcodebuild` is unavailable — everything goes through Swift Package
Manager plus a packaging script.

## Targets

| Target | What it is |
|---|---|
| `VoiceFlowCore` | Library: hotkey, audio capture, whisper.cpp wrapper, model manager, settings, permissions, text injection |
| `VoiceFlowApp` | Executable: menu bar UI (`NSStatusItem`), wires `VoiceFlowCore` together |
| `LatencySpike` | Executable: diagnostic tool that measures hotkey→transcript latency |

## Commands

```bash
swift build                      # debug build of everything
swift test                       # run the XCTest suite (VoiceFlowCoreTests)
swift test --filter <TestClass>   # run one test class
./scripts/package-app.sh         # release build + assemble dist/VoiceFlow.app + ad-hoc sign
open dist/VoiceFlow.app          # launch the packaged app
swift run LatencySpike <model>   # latency spike, needs a path to a ggml .bin model
```

## Always test through the packaged app

`LSUIElement` (no Dock icon) and `NSMicrophoneUsageDescription` live in
`Resources/VoiceFlowApp/Info.plist`, which only takes effect inside a real `.app`
bundle. Running the raw binary with `swift run VoiceFlowApp` **crashes** the process
the moment it requests microphone access, because macOS refuses privacy-sensitive
access without a usage description in the running bundle's `Info.plist`.

So: verify app behavior with `./scripts/package-app.sh && open dist/VoiceFlow.app`,
never with `swift run VoiceFlowApp`.

## Permissions the app needs

- **Microphone** — System Settings → Privacy & Security → Microphone
- **Accessibility** — System Settings → Privacy & Security → Accessibility (required
  to type text into other apps)

After a rebuild the app keeps its TCC grants as long as the ad-hoc signature identity
stays stable. If macOS stops recognizing the app, remove it from both privacy lists
and re-grant.

## Model files

Whisper models live in `~/Library/Application Support/VoiceFlow/models/` and are
downloaded at first run — never bundled in the binary, never committed.
