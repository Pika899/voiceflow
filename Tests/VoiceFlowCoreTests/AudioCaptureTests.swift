import Testing
import AVFoundation
@testable import VoiceFlowCore

@Suite struct AudioCaptureTests {
    @Test func resampleDownsamples44100To16000() throws {
        let inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
        let targetFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!

        let frameCount: AVAudioFrameCount = 44100 // 1 second of audio
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: frameCount)!
        buffer.frameLength = frameCount
        for i in 0..<Int(frameCount) {
            buffer.floatChannelData![0][i] = sin(Float(i) * 0.01)
        }

        let output = AudioCapture.resample(buffer: buffer, from: inputFormat, to: targetFormat)

        // ~1 second of audio at 16kHz should be close to 16000 samples.
        #expect(output.count > 15000)
        #expect(output.count < 17000)
    }

    @Test func resampleOfEmptyBufferIsEmpty() throws {
        let inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
        let targetFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!
        let buffer = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: 0)!
        buffer.frameLength = 0

        let output = AudioCapture.resample(buffer: buffer, from: inputFormat, to: targetFormat)

        #expect(output.count == 0)
    }
}
