import Foundation
import CryptoKit

public struct ModelInfo {
    public let name: String
    public let fileName: String
    public let downloadURL: URL
    public let sha256: String

    public init(name: String, fileName: String, downloadURL: URL, sha256: String) {
        self.name = name
        self.fileName = fileName
        self.downloadURL = downloadURL
        self.sha256 = sha256
    }
}

public enum ModelManagerError: Error {
    case checksumMismatch(expected: String, actual: String)
    case downloadFailed(underlying: Error)
}

public final class ModelManager {
    // SHA256 values verified on this machine with `shasum -a 256` against the
    // actual downloaded files from huggingface.co/ggerganov/whisper.cpp (see
    // task-6-report.md for the verbatim shasum output).
    public static let knownModels: [ModelInfo] = [
        ModelInfo(
            name: "base",
            fileName: "ggml-base.bin",
            downloadURL: URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin")!,
            sha256: "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"
        ),
        ModelInfo(
            name: "small",
            fileName: "ggml-small.bin",
            downloadURL: URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin")!,
            sha256: "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"
        )
    ]

    private let modelsDirectory: URL

    public init(modelsDirectory: URL = ModelManager.defaultModelsDirectory()) {
        self.modelsDirectory = modelsDirectory
    }

    public static func defaultModelsDirectory() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("VoiceFlow/models", isDirectory: true)
    }

    public func localPath(for model: ModelInfo) -> URL {
        modelsDirectory.appendingPathComponent(model.fileName)
    }

    public func isModelPresentAndValid(_ model: ModelInfo) -> Bool {
        let path = localPath(for: model)
        guard FileManager.default.fileExists(atPath: path.path) else { return false }
        guard let actual = try? Self.sha256(ofFileAt: path) else { return false }
        return actual == model.sha256
    }

    public static func sha256(ofFileAt url: URL) throws -> String {
        // Streamed in chunks: model files are 150-500 MB and this runs on
        // every launch, so loading the whole file into memory is not acceptable
        // on the modest hardware this project explicitly targets.
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        let chunkSize = 1 << 20
        while true {
            let chunk = try handle.read(upToCount: chunkSize) ?? Data()
            if chunk.isEmpty { break }
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    public func download(
        _ model: ModelInfo,
        progress: @escaping (Double) -> Void,
        completion: @escaping (Result<URL, ModelManagerError>) -> Void
    ) {
        try? FileManager.default.createDirectory(at: modelsDirectory, withIntermediateDirectories: true)
        let destination = localPath(for: model)

        let task = URLSession.shared.downloadTask(with: model.downloadURL) { tempURL, _, error in
            if let error {
                completion(.failure(.downloadFailed(underlying: error)))
                return
            }
            guard let tempURL else {
                completion(.failure(.downloadFailed(underlying: URLError(.badServerResponse))))
                return
            }
            do {
                if FileManager.default.fileExists(atPath: destination.path) {
                    try FileManager.default.removeItem(at: destination)
                }
                try FileManager.default.moveItem(at: tempURL, to: destination)
                let actual = try Self.sha256(ofFileAt: destination)
                guard actual == model.sha256 else {
                    try? FileManager.default.removeItem(at: destination)
                    completion(.failure(.checksumMismatch(expected: model.sha256, actual: actual)))
                    return
                }
                completion(.success(destination))
            } catch {
                completion(.failure(.downloadFailed(underlying: error)))
            }
        }

        let observation = task.progress.observe(\.fractionCompleted) { fractionCompleted, _ in
            progress(fractionCompleted.fractionCompleted)
        }
        objc_setAssociatedObject(task, &Self.progressObservationKey, observation, .OBJC_ASSOCIATION_RETAIN)
        task.resume()
    }

    nonisolated(unsafe) private static var progressObservationKey: UInt8 = 0
}
