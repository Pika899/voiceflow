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
