import Foundation
import CWhisper

public struct TranscriptionResult {
    public let text: String
    public let durationSeconds: Double
}

public enum WhisperEngineError: Error {
    case modelLoadFailed(path: String)
    case inferenceFailed
}

public final class WhisperEngine {
    private let context: OpaquePointer

    public init(modelPath: String) throws {
        // `whisper_init_from_file` is deprecated in whisper.cpp v1.9.4; the
        // params variant is the supported entry point, and its default
        // `use_gpu` lets inference reach Metal on Apple Silicon.
        let contextParams = whisper_context_default_params()
        guard let ctx = whisper_init_from_file_with_params(modelPath, contextParams) else {
            throw WhisperEngineError.modelLoadFailed(path: modelPath)
        }
        self.context = ctx
    }

    deinit {
        whisper_free(context)
    }

    public func transcribe(samples: [Float], language: String) throws -> TranscriptionResult {
        // Nothing captured (hotkey tapped without speaking): don't hand
        // whisper a null buffer, just report an empty transcription.
        guard !samples.isEmpty else {
            return TranscriptionResult(text: "", durationSeconds: 0)
        }

        let start = Date()
        var params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY)
        params.print_progress = false
        params.print_realtime = false

        let status: Int32 = language.withCString { langPtr in
            params.language = langPtr
            return samples.withUnsafeBufferPointer { buffer in
                whisper_full(context, params, buffer.baseAddress, Int32(buffer.count))
            }
        }
        guard status == 0 else { throw WhisperEngineError.inferenceFailed }

        let segmentCount = whisper_full_n_segments(context)
        var text = ""
        for i in 0..<segmentCount {
            // A null segment after a successful whisper_full is a whisper.cpp
            // anomaly; surface it rather than returning quietly truncated text.
            guard let cText = whisper_full_get_segment_text(context, i) else {
                throw WhisperEngineError.inferenceFailed
            }
            text += String(cString: cText)
        }

        return TranscriptionResult(
            text: Self.trim(text),
            durationSeconds: Date().timeIntervalSince(start)
        )
    }

    static func trim(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
