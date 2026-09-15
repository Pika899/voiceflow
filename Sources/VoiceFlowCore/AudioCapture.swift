import AVFoundation

public enum AudioCaptureError: Error {
    /// The input node reports no usable device (0 channels or 0 Hz).
    case noInputDevice
    /// AVAudioConverter refused the device's format → 16 kHz mono.
    case unsupportedInputFormat
}

public final class AudioCapture {
    public private(set) var isCapturing = false

    private let engine = AVAudioEngine()
    // `samples` is appended on the audio render thread (inside the tap) and
    // read/cleared on the caller's thread; every access goes through `lock`.
    private let lock = NSLock()
    private var samples: [Float] = []
    private var converter: AVAudioConverter?
    private let targetFormat = AVAudioFormat(
        commonFormat: .pcmFormatFloat32,
        sampleRate: 16000,
        channels: 1,
        interleaved: false
    )!

    public init() {}

    public func start() throws {
        guard !isCapturing else { return }

        let inputNode = engine.inputNode
        let inputFormat = inputNode.inputFormat(forBus: 0)
        // installTap on a 0-channel/0 Hz format raises an Objective-C
        // exception that Swift `try` cannot catch — fail loudly here instead.
        guard inputFormat.channelCount > 0, inputFormat.sampleRate > 0 else {
            throw AudioCaptureError.noInputDevice
        }
        // One converter for the whole capture: building one per tap callback
        // would re-prime its filter on every buffer and drop samples at each
        // boundary, and would make a construction failure invisible.
        guard let converter = AVAudioConverter(from: inputFormat, to: targetFormat) else {
            throw AudioCaptureError.unsupportedInputFormat
        }
        self.converter = converter

        lock.lock()
        samples.removeAll()
        lock.unlock()

        inputNode.installTap(onBus: 0, bufferSize: 1024, format: inputFormat) { [weak self] buffer, _ in
            guard let self else { return }
            let converted = Self.resample(buffer: buffer, using: converter, to: self.targetFormat)
            self.lock.lock()
            self.samples.append(contentsOf: converted)
            self.lock.unlock()
        }

        engine.prepare()
        do {
            try engine.start()
        } catch {
            inputNode.removeTap(onBus: 0)
            self.converter = nil
            throw error
        }
        isCapturing = true
    }

    public func stop() -> [Float] {
        guard isCapturing else { return [] }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        isCapturing = false
        converter = nil

        lock.lock()
        defer { lock.unlock() }
        return samples
    }

    static func resample(buffer: AVAudioPCMBuffer, using converter: AVAudioConverter, to targetFormat: AVAudioFormat) -> [Float] {
        guard buffer.frameLength > 0 else { return [] }

        let ratio = targetFormat.sampleRate / converter.inputFormat.sampleRate
        // Slack covers the converter's internal filter delay, which can emit a
        // few frames beyond the pure ratio on a given call.
        let outputCapacity = AVAudioFrameCount((Double(buffer.frameLength) * ratio).rounded(.up)) + 64
        guard let outputBuffer = AVAudioPCMBuffer(pcmFormat: targetFormat, frameCapacity: outputCapacity) else { return [] }

        var error: NSError?
        var didProvideInput = false
        converter.convert(to: outputBuffer, error: &error) { _, outStatus in
            if didProvideInput {
                outStatus.pointee = .noDataNow
                return nil
            }
            didProvideInput = true
            outStatus.pointee = .haveData
            return buffer
        }

        guard error == nil, let channelData = outputBuffer.floatChannelData else { return [] }
        let frameLength = Int(outputBuffer.frameLength)
        return Array(UnsafeBufferPointer(start: channelData[0], count: frameLength))
    }
}
