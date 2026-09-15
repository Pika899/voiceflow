import Foundation
import AppKit
import VoiceFlowCore

// Point this at a real model you've downloaded manually, e.g.:
// curl -L -o /tmp/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
guard CommandLine.arguments.count > 1 else {
    print("Usage: swift run LatencySpike <path-to-ggml-model.bin>")
    exit(1)
}
let modelPath = CommandLine.arguments[1]

print("Loading model at \(modelPath)...")
let loadStart = Date()
let engine = try WhisperEngine(modelPath: modelPath)
print("Model loaded in \(Date().timeIntervalSince(loadStart))s")

let audioCapture = AudioCapture()
let hotkey = HotkeyManager()

guard hotkey.register() else {
    print("Failed to register hotkey — is another app using Control+Option+Space?")
    exit(1)
}

var pressTime: Date?

hotkey.onPress = {
    pressTime = Date()
    do {
        try audioCapture.start()
        print("\n[listening...] speak now, release the hotkey when done")
    } catch {
        print("Failed to start audio capture: \(error)")
    }
}

hotkey.onRelease = {
    guard let pressTime else { return }
    let releaseTime = Date()
    let samples = audioCapture.stop()
    print("Captured \(samples.count) samples (\(Double(samples.count) / 16000.0)s of audio) in \(releaseTime.timeIntervalSince(pressTime))s")

    do {
        let result = try engine.transcribe(samples: samples, language: "it")
        let totalLatency = Date().timeIntervalSince(releaseTime)
        print("Transcription: \"\(result.text)\"")
        print("Inference time: \(result.durationSeconds)s")
        print("Total latency from hotkey release to text ready: \(totalLatency)s")
    } catch {
        print("Transcription failed: \(error)")
    }
}

print("Ready. Hold Control+Option+Space, speak, then release.")
RunLoop.main.run()
