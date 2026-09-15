import AppKit
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

            if let launchAtLoginError = viewModel.launchAtLoginError {
                Text(launchAtLoginError)
                    .font(.caption)
                    .foregroundStyle(.red)
            }

            Text("Hotkey: Control+Option+Space").foregroundStyle(.secondary)

            // A menu bar app has no Dock icon and no menu bar menu, so this
            // is the only way to quit it.
            Divider()
            Button("Quit VoiceFlow") { NSApp.terminate(nil) }
        }
        .padding()
        .frame(width: 260)
    }
}

final class SettingsViewModel: ObservableObject {
    private let store: SettingsStore
    private var isRevertingLaunchAtLogin = false

    @Published var model: WhisperModelName {
        didSet { store.model = model }
    }
    @Published var language: DictationLanguage {
        didSet { store.language = language }
    }
    @Published var launchAtLogin: Bool {
        didSet {
            guard !isRevertingLaunchAtLogin, launchAtLogin != oldValue else { return }
            do {
                if launchAtLogin {
                    try SMAppService.mainApp.register()
                } else {
                    try SMAppService.mainApp.unregister()
                }
                store.launchAtLogin = launchAtLogin
                launchAtLoginError = nil
            } catch {
                // Never let the toggle claim a state the OS refused: put it
                // back and say why, instead of persisting a lie.
                isRevertingLaunchAtLogin = true
                launchAtLogin = oldValue
                isRevertingLaunchAtLogin = false
                launchAtLoginError = "Couldn't update Login Items: \(error.localizedDescription)"
            }
        }
    }
    @Published var launchAtLoginError: String?

    init(store: SettingsStore) {
        self.store = store
        self.model = store.model
        self.language = store.language
        self.launchAtLogin = store.launchAtLogin
    }
}
