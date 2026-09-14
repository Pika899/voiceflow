import Testing
import CWhisper
@testable import VoiceFlowCore

@Suite struct PackageScaffoldTests {
    @Test func coreVersionIsExposed() {
        #expect(voiceFlowCoreVersion == "1.0.0")
    }

    @Test func whisperLibraryIsLinked() {
        // whisper_print_system_info returns a C string describing the build.
        // Calling it proves the vendored static library is linked and callable.
        let info = String(cString: whisper_print_system_info())
        #expect(!info.isEmpty)
    }
}
