import AVFoundation

public final class AudioCapture {
    public private(set) var isCapturing = false

    private let engine = AVAudioEngine()
    private var samples: [Float] = []
    private let targetFormat = AVAudioFormat(
        commonFormat: .pcmFormatFloat32,
        sampleRate: 16000,
        channels: 1,
        interleaved: false
    )!

    public init() {}

    public func start() throws {
        guard !isCapturing else { return }
        samples.removeAll()

        let inputNode = engine.inputNode
        let inputFormat = inputNode.inputFormat(forBus: 0)

        inputNode.installTap(onBus: 0, bufferSize: 1024, format: inputFormat) { [weak self] buffer, _ in
            guard let self else { return }
            let converted = Self.resample(buffer: buffer, from: inputFormat, to: self.targetFormat)
            self.samples.append(contentsOf: converted)
        }

        engine.prepare()
        try engine.start()
        isCapturing = true
    }

    public func stop() -> [Float] {
        guard isCapturing else { return samples }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        isCapturing = false
        return samples
    }

    static func resample(buffer: AVAudioPCMBuffer, from inputFormat: AVAudioFormat, to targetFormat: AVAudioFormat) -> [Float] {
        guard buffer.frameLength > 0 else { return [] }
        guard let converter = AVAudioConverter(from: inputFormat, to: targetFormat) else { return [] }

        let ratio = targetFormat.sampleRate / inputFormat.sampleRate
        let outputCapacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 16
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
