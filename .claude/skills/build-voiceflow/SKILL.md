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
swift build                          # debug build of everything
./scripts/test.sh                    # run the Swift Testing suite — use this, NOT `swift test`
./scripts/test.sh --filter <Suite>   # run one suite, e.g. --filter SettingsStoreTests
./scripts/build-whisper.sh           # one-time: vendor + statically build whisper.cpp v1.9.4
./scripts/make-signing-cert.sh       # one-time: local "VoiceFlow Dev" cert for a stable signing identity
./scripts/package-app.sh             # release build + assemble dist/VoiceFlow.app (signs with VoiceFlow Dev if present)
open dist/VoiceFlow.app              # launch the packaged app
swift run LatencySpike <model>       # latency spike, needs a path to a ggml .bin model
```

## Why the test wrapper

Tests use **Swift Testing** (`import Testing`, `@Suite`, `@Test`, `#expect`) — XCTest
ships only inside Xcode.app and is absent here. This CLT toolchain keeps the Testing
macro plugin in `usr/lib/swift/host/plugins/testing/`, which the compiler only
sometimes scans, so plain `swift test` fails intermittently with
"plugin for module 'TestingMacros' not found". `scripts/test.sh` passes the plugin path
explicitly (`-Xswiftc -plugin-path`) and is deterministic. If you ever see that error,
you ran `swift test` directly — use the wrapper.

The first test run after a clean build takes ~15 s: the whisper linkage test
JIT-compiles Metal shaders once. Subsequent runs are fast.

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

TCC grants are keyed to the app's code-signing identity. An ad-hoc signature's
identity is the cdhash of the exact binary, so it changes on EVERY rebuild and the
grants silently stop applying (the toggle still shows ON). Run
`scripts/make-signing-cert.sh` once: `package-app.sh` then signs with the local
"VoiceFlow Dev" certificate and the identity stays stable across rebuilds. The first
signing pops a keychain dialog — click "Always Allow". If grants ever seem ignored
after an ad-hoc build, remove the app from the Accessibility list and add it back.

## Model files

Whisper models live in `~/Library/Application Support/VoiceFlow/models/` and are
downloaded at first run — never bundled in the binary, never committed.
