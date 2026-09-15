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
