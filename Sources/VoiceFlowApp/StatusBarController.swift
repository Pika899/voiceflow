import AppKit
import SwiftUI
import VoiceFlowCore

enum DictationState {
    case idle
    case listening
    case transcribing
    case error(String)
}

final class StatusBarController: NSObject {
    private let statusItem: NSStatusItem
    private let hotkeyManager = HotkeyManager()
    private let audioCapture = AudioCapture()
    private let permissionsManager = PermissionsManager()
    private let textInjector = TextInjector()
    private let settingsStore = SettingsStore()
    private let modelManager = ModelManager()
    private var whisperEngine: WhisperEngine?

    private lazy var popover: NSPopover = {
        let popover = NSPopover()
        popover.contentSize = NSSize(width: 260, height: 220)
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(rootView: SettingsPopoverView(viewModel: SettingsViewModel(store: settingsStore)))
        return popover
    }()

    private var state: DictationState = .idle {
        didSet { updateIcon() }
    }

    override init() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        updateIcon()
        statusItem.button?.action = #selector(togglePopover)
        statusItem.button?.target = self
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
        permissionsManager.requestMicrophoneAccess { [weak self] granted in
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
        // A press while the previous inference is still running must not
        // restart capture: the old completion would later overwrite
        // `.listening` and the new recording would be silently dropped.
        switch state {
        case .listening, .transcribing:
            return
        case .idle, .error:
            break
        }
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
                    } catch TextInjectionError.accessibilityNotTrusted {
                        self.state = .error("accessibility-permission")
                    } catch {
                        self.state = .error("injection-failed")
                    }
                }
            } catch {
                DispatchQueue.main.async { self.state = .error("transcription-failed") }
            }
        }
    }

    @objc private func togglePopover() {
        guard let button = statusItem.button else { return }
        if popover.isShown {
            popover.performClose(nil)
        } else {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        }
    }
}
