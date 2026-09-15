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
    private let fnKeyMonitor = FnKeyMonitor()
    private let audioCapture = AudioCapture()
    private let permissionsManager = PermissionsManager()
    private let textInjector = TextInjector()
    private let settingsStore = SettingsStore()
    private let modelManager = ModelManager()
    private var whisperEngine: WhisperEngine?

    private lazy var popover: NSPopover = {
        let popover = NSPopover()
        popover.contentSize = NSSize(width: 260, height: 310)
        popover.behavior = .transient
        let viewModel = SettingsViewModel(store: settingsStore) { [weak self] in
            self?.configurePushToTalk()
        }
        popover.contentViewController = NSHostingController(rootView: SettingsPopoverView(viewModel: viewModel))
        return popover
    }()

    /// Only ever touched on the main queue: written by the timeout work item
    /// (fires on the main queue) and read by the transcription worker's
    /// main-queue completion block once inference returns (Ruling R9).
    private var dictationTimedOut = false

    // `NSAlert.runModal()` pumps the run loop, so a hotkey press can arrive while
    // an alert is up and drive the state machine back into `.error` — without
    // this flag that would stack a second alert on top of the first.
    private var isPresentingAlert = false

    private var state: DictationState = .idle {
        didSet {
            updateIcon()
            if case .error(let code) = state, !isPresentingAlert {
                presentAlert(for: code)
            }
        }
    }

    override init() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        updateIcon()
        statusItem.button?.action = #selector(togglePopover)
        statusItem.button?.target = self
        requestMicrophoneAccessIfNeeded()
        loadModelIfPresent()
        configurePushToTalk()

        // Proactive check: detect missing Accessibility access at launch
        // rather than waiting for the first injection to fail silently.
        // (The fn-key backend may already have reported it just above —
        // don't raise the same alert twice.)
        if permissionsManager.accessibilityStatus() != .granted,
           !isReportingAccessibilityError {
            state = .error("accessibility-permission")
        }
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

    /// Binds push-to-talk to whichever key the user chose. Safe to call again
    /// after the choice changes or after Accessibility is granted: both
    /// backends are torn down first.
    private var isReportingAccessibilityError: Bool {
        if case .error(let code) = state, code == "accessibility-permission" {
            return true
        }
        return false
    }

    private func configurePushToTalk() {
        // fn is a modifier, so the user can hold it and still reach the
        // popover with the mouse: close an in-flight dictation before the
        // backend that would have delivered its release goes away.
        if case .listening = state {
            finishDictation()
        }
        hotkeyManager.unregister()
        fnKeyMonitor.stop()

        switch settingsStore.pushToTalkKey {
        case .fnKey:
            fnKeyMonitor.onPress = { [weak self] in self?.beginDictation() }
            fnKeyMonitor.onRelease = { [weak self] in self?.finishDictation() }
            if !fnKeyMonitor.start() {
                // Same Accessibility permission as text injection. Report it
                // here too: this runs again from the popover and after the
                // alert, where no other check would surface a dead key.
                state = .error("accessibility-permission")
            }
        case .controlOptionSpace:
            hotkeyManager.onPress = { [weak self] in self?.beginDictation() }
            hotkeyManager.onRelease = { [weak self] in self?.finishDictation() }
            if !hotkeyManager.register() {
                state = .error("hotkey-conflict")
            }
        }
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

        // Ruling R9: `dictationTimedOut` is written by this timer (main queue)
        // and read by the worker's main-queue completion block below — never
        // touched off the main queue, so no separate synchronization is needed.
        //
        // Honest limitation: `whisper_full` (inside `transcribe`) is
        // synchronous and blocking, and it is NOT aborted when this timeout
        // fires. This only changes what the UI reports after 15 seconds; the
        // background thread keeps running inference until it returns, and its
        // late result is simply dropped below. This is a UI-visible timeout,
        // not a cancellation.
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
                // Injection drives the Accessibility API and posts CGEvents,
                // both of which belong on the main thread.
                DispatchQueue.main.async {
                    timeoutWorkItem.cancel()
                    // The UI already reported a timeout; drop the late result
                    // rather than injecting text the user has stopped expecting.
                    guard !self.dictationTimedOut else { return }
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
                DispatchQueue.main.async {
                    timeoutWorkItem.cancel()
                    guard !self.dictationTimedOut else { return }
                    self.state = .error("transcription-failed")
                }
            }
        }
    }

    // Identifies the download whose completion is still welcome. Cancel or a
    // newer download replaces it, so a stale completion can neither dismiss an
    // unrelated alert nor overwrite whatever state the user has moved on to.
    private var activeDownloadID = UUID()

    /// Downloads the currently configured model with a visible, non-silent
    /// progress bar — this is the app's only network call.
    private func startModelDownload() {
        let model = ModelManager.knownModels.first { $0.name == settingsStore.model.rawValue }!
        let downloadID = UUID()
        activeDownloadID = downloadID

        let progressAlert = NSAlert()
        progressAlert.messageText = "Downloading \(model.name) model..."
        progressAlert.addButton(withTitle: "Cancel")
        let progressBar = NSProgressIndicator(frame: NSRect(x: 0, y: 0, width: 250, height: 20))
        progressBar.style = .bar
        progressBar.minValue = 0
        progressBar.maxValue = 1
        progressAlert.accessoryView = progressBar

        modelManager.download(model, progress: { fraction in
            DispatchQueue.main.async { progressBar.doubleValue = fraction }
        }, completion: { [weak self] result in
            DispatchQueue.main.async {
                guard let self, self.activeDownloadID == downloadID else { return }
                // Only dismiss our own progress alert — never whatever else
                // may be modal by the time a long download finishes.
                if NSApp.modalWindow == progressAlert.window {
                    NSApp.stopModal()
                }
                switch result {
                case .success:
                    self.loadModelIfPresent()
                    if self.whisperEngine != nil {
                        self.state = .idle
                    }
                case .failure(let error):
                    self.state = .error("model-download-failed: \(error)")
                }
            }
        })

        let response = progressAlert.runModal()
        if response == .alertFirstButtonReturn {
            // Cancel: the URLSession task keeps running in the background (v1
            // limitation — ModelManager exposes no cancel), but its completion
            // is now ignored. State is still `.error("model-missing")` from the
            // alert that led here; don't reassign it, or it would re-prompt.
            activeDownloadID = UUID()
        } else if case .error(let code) = state {
            // The completion ran while the outer alert was still on the stack,
            // so its `.error` didSet was (correctly) not allowed to stack a
            // dialog. Now that the progress alert is gone, show it explicitly —
            // a failed download or unloadable model must never end in silence.
            presentAlert(for: code)
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

extension StatusBarController {
    /// Maps every error code the state machine can produce to spec-required,
    /// user-visible guidance. No error code reaches this app's UI silently.
    func presentAlert(for errorCode: String) {
        let wasPresentingAlert = isPresentingAlert
        isPresentingAlert = true
        defer { isPresentingAlert = wasPresentingAlert }
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
            // If the grant just happened, the fn monitor could not start at
            // launch; rebinding now picks it up without a relaunch.
            if settingsStore.pushToTalkKey == .fnKey {
                configurePushToTalk()
            }

        case "model-missing":
            alert.messageText = "Model not downloaded"
            alert.informativeText = "The speech model hasn't been downloaded yet, or failed a corruption check. Download it now?"
            alert.addButton(withTitle: "Download")
            alert.addButton(withTitle: "Cancel")
            if alert.runModal() == .alertFirstButtonReturn {
                startModelDownload()
            }

        case "transcription-failed", "transcription-timeout":
            alert.messageText = "Transcription failed"
            alert.informativeText = "Something went wrong during transcription. Try again."
            alert.addButton(withTitle: "OK")
            alert.runModal()

        case "audio-start-failed":
            alert.messageText = "Couldn't start recording"
            alert.informativeText = "VoiceFlow couldn't open the microphone. Check that one is connected and not in use by another app, then try again."
            alert.addButton(withTitle: "OK")
            alert.runModal()

        case "injection-failed":
            alert.messageText = "Couldn't type the text"
            alert.informativeText = "The transcription succeeded, but VoiceFlow couldn't insert it into the active app. Click into a text field and try again."
            alert.addButton(withTitle: "OK")
            alert.runModal()

        case "model-load-failed":
            // Checksum passed but whisper refused the file: offer the same
            // re-download path as a missing model rather than a bare error.
            alert.messageText = "Model couldn't be loaded"
            alert.informativeText = "The speech model is present but failed to load. Download it again?"
            alert.addButton(withTitle: "Download")
            alert.addButton(withTitle: "Cancel")
            if alert.runModal() == .alertFirstButtonReturn {
                startModelDownload()
            }

        case "hotkey-conflict":
            alert.messageText = "Hotkey already in use"
            // v1 has no rebind UI, so the only honest advice is to free the shortcut.
            alert.informativeText = "Control+Option+Space is already registered by another app. Quit that app or free the shortcut there, then relaunch VoiceFlow."
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
