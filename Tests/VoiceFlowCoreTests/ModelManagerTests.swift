import Foundation
import Testing
@testable import VoiceFlowCore

@Suite final class ModelManagerTests {
    let tempDir: URL
    let manager: ModelManager

    // Swift Testing creates a fresh suite instance per test, so init/deinit
    // are the per-test setup and teardown.
    init() {
        tempDir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try? FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        manager = ModelManager(modelsDirectory: tempDir)
    }

    deinit {
        try? FileManager.default.removeItem(at: tempDir)
    }

    // The app resolves the chosen model with `knownModels.first { … }!`:
    // a settings case without a catalogue entry would crash at launch.
    @Test func everySettingsModelHasACatalogueEntry() {
        for model in WhisperModelName.allCases {
            #expect(ModelManager.knownModels.contains { $0.name == model.rawValue }, "\(model) missing from knownModels")
        }
    }

    @Test func mediumModelIsKnown() {
        let medium = ModelManager.knownModels.first { $0.name == "medium" }
        #expect(medium?.fileName == "ggml-medium.bin")
        #expect(medium?.downloadURL.absoluteString == "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin")
        #expect(medium?.sha256.count == 64)
    }

    @Test func localPathAppendsFileNameToModelsDirectory() {
        let model = ModelInfo(name: "base", fileName: "ggml-base.bin", downloadURL: URL(string: "https://example.com/ggml-base.bin")!, sha256: "irrelevant-for-this-test")
        #expect(manager.localPath(for: model) == tempDir.appendingPathComponent("ggml-base.bin"))
    }

    @Test func isModelPresentAndValidIsFalseWhenFileMissing() {
        let model = ModelInfo(name: "base", fileName: "missing.bin", downloadURL: URL(string: "https://example.com/missing.bin")!, sha256: "does-not-matter")
        #expect(!manager.isModelPresentAndValid(model))
    }

    @Test func isModelPresentAndValidIsFalseWhenChecksumMismatches() throws {
        let fileURL = tempDir.appendingPathComponent("corrupt.bin")
        try "not the real model".write(to: fileURL, atomically: true, encoding: .utf8)
        let model = ModelInfo(name: "base", fileName: "corrupt.bin", downloadURL: URL(string: "https://example.com/corrupt.bin")!, sha256: "0000000000000000000000000000000000000000000000000000000000000000")
        #expect(!manager.isModelPresentAndValid(model))
    }

    @Test func isModelPresentAndValidIsTrueWhenChecksumMatches() throws {
        let fileURL = tempDir.appendingPathComponent("fixture.bin")
        try "voiceflow-test-fixture\n".write(to: fileURL, atomically: true, encoding: .utf8)
        // Real SHA256 of the literal bytes "voiceflow-test-fixture\n", computed with:
        // printf 'voiceflow-test-fixture\n' | shasum -a 256
        let model = ModelInfo(name: "fixture", fileName: "fixture.bin", downloadURL: URL(string: "https://example.com/fixture.bin")!, sha256: "f8a7289ca2e97501bf779dfc71be00dda2b3e4bdecc94a0eef00451e643e04b3")
        #expect(manager.isModelPresentAndValid(model))
    }
}
