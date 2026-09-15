import Foundation

public enum WhisperModelName: String, CaseIterable, Codable {
    case base
    case small
    case medium
}

public enum DictationLanguage: String, CaseIterable, Codable {
    case italian = "it"
    case english = "en"
}

public enum PushToTalkKey: String, CaseIterable, Codable {
    case fnKey = "fn"
    case controlOptionSpace = "control-option-space"
}

public final class SettingsStore {
    private enum Key {
        static let model = "voiceflow.model"
        static let language = "voiceflow.language"
        static let launchAtLogin = "voiceflow.launchAtLogin"
        static let pushToTalkKey = "voiceflow.pushToTalkKey"
        static let playSounds = "voiceflow.playSounds"
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

    public var playSounds: Bool {
        // `bool(forKey:)` reads a missing key as false; the default is on.
        get { defaults.object(forKey: Key.playSounds) as? Bool ?? true }
        set { defaults.set(newValue, forKey: Key.playSounds) }
    }

    public var pushToTalkKey: PushToTalkKey {
        get { PushToTalkKey(rawValue: defaults.string(forKey: Key.pushToTalkKey) ?? "") ?? .fnKey }
        set { defaults.set(newValue.rawValue, forKey: Key.pushToTalkKey) }
    }
}
