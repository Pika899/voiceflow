import Testing
@testable import VoiceFlowCore

// .serialized: every test registers the same global combination, so they
// must not overlap.
@Suite(.serialized) struct HotkeyManagerTests {
    @Test func registersAndUnregisters() {
        let manager = HotkeyManager()
        #expect(manager.register())
        manager.unregister()
    }

    @Test func secondRegistrationOfSameComboIsRefused() {
        let first = HotkeyManager()
        let second = HotkeyManager()
        defer {
            first.unregister()
            second.unregister()
        }
        #expect(first.register())
        // Carbon refuses a combination that is already registered — by any
        // process, including this one — with eventHotKeyExistsErr. This is
        // exactly the "hotkey already in use" case the spec requires detecting.
        #expect(!second.register())
    }

    @Test func comboBecomesAvailableAgainAfterUnregister() {
        let first = HotkeyManager()
        let second = HotkeyManager()
        defer { second.unregister() }
        #expect(first.register())
        first.unregister()
        #expect(second.register())
    }

    @Test func registeringTwiceOnSameManagerIsIdempotent() {
        let manager = HotkeyManager()
        defer { manager.unregister() }
        #expect(manager.register())
        #expect(manager.register())
    }
}
