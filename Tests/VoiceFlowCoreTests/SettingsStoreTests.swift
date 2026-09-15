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

    @Test func defaultPushToTalkKeyIsFn() {
        #expect(store.pushToTalkKey == .fnKey)
    }

    @Test func pushToTalkKeyRoundTrips() {
        store.pushToTalkKey = .controlOptionSpace
        #expect(store.pushToTalkKey == .controlOptionSpace)
    }
}
