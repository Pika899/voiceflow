import AppKit

/// Short audible cues for dictation start/stop, taken from the system sound
/// set so nothing has to be bundled with the app. A missing sound file
/// (unlikely, but possible on some macOS versions) just means silence.
final class SoundPlayer {
    private let start = NSSound(contentsOfFile: "/System/Library/Sounds/Tink.aiff", byReference: true)
    private let stop = NSSound(contentsOfFile: "/System/Library/Sounds/Pop.aiff", byReference: true)

    func playStart() {
        play(start)
    }

    func playStop() {
        play(stop)
    }

    private func play(_ sound: NSSound?) {
        guard let sound else { return }
        // Restart cleanly if the previous cue is still playing.
        sound.stop()
        sound.play()
    }
}
