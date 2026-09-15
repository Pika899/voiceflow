import Testing
import AVFoundation
@testable import VoiceFlowCore

@Suite struct AudioCaptureTests {
    private let inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
    private let targetFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!

    @Test func resampleDownsamples44100To16000() throws {
        let converter = try #require(AVAudioConverter(from: inputFormat, to: targetFormat))
        let frameCount: AVAudioFrameCount = 44100 // 1 second of audio
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: frameCount)!
        buffer.frameLength = frameCount
        for i in 0..<Int(frameCount) {
            buffer.floatChannelData![0][i] = sin(Float(i) * 0.01)
        }

        let output = AudioCapture.resample(buffer: buffer, using: converter, to: targetFormat)

        // One second at 16 kHz is 16000 frames. A single-shot conversion loses
        // a few dozen frames to the converter's filter priming (observed: 15994
        // on this machine), so allow that — but nothing that would indicate
        // dropped audio.
        #expect(output.count >= 15900)
        #expect(output.count <= 16000)
    }

    @Test func resampleOfEmptyBufferIsEmpty() throws {
        let converter = try #require(AVAudioConverter(from: inputFormat, to: targetFormat))
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: 0)!
        buffer.frameLength = 0

        let output = AudioCapture.resample(buffer: buffer, using: converter, to: targetFormat)

        #expect(output.count == 0)
    }

    @Test func resampleAcrossConsecutiveBuffersPreservesTotalLength() throws {
        // Capture feeds the same converter ~1024-frame buffers back to back.
        // Reusing one converter across buffers must not lose frames at each
        // boundary the way a fresh converter per buffer would.
        let converter = try #require(AVAudioConverter(from: inputFormat, to: targetFormat))
        let chunk: AVAudioFrameCount = 1024
        let chunks = 43 // 43 * 1024 = 44032 frames ≈ 0.9985 s
        var total = 0
        for c in 0..<chunks {
            let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: chunk)!
            buffer.frameLength = chunk
            for i in 0..<Int(chunk) {
                buffer.floatChannelData![0][i] = sin(Float(c * Int(chunk) + i) * 0.01)
            }
            total += AudioCapture.resample(buffer: buffer, using: converter, to: targetFormat).count
        }

        // 44032 / 44100 * 16000 ≈ 15975 frames if nothing is lost across
        // boundaries; same priming allowance as the single-buffer test.
        #expect(total >= 15875)
        #expect(total <= 15975)
    }
}
