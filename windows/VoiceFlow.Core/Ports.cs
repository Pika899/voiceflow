namespace VoiceFlow.Core;

/// <summary>Captures microphone audio for the duration of a push-to-talk press.</summary>
public interface IAudioCapture
{
    /// <summary>Throws <see cref="AudioCaptureException"/> with code "microphone-permission" or "audio-start-failed".</summary>
    void Start();

    float[] Stop();
}

public sealed class AudioCaptureException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>The push-to-talk key backend (global hotkey or modifier-key monitor).</summary>
public interface IHotkey
{
    event Action? Pressed;
    event Action? Released;

    /// <summary>
    /// The press that started the current dictation turned out to be a
    /// keyboard shortcut, not a dictation (e.g. Ctrl+C while the Ctrl backend
    /// is active) — the controller must abandon the capture without
    /// transcribing. A backend that can never distinguish a shortcut from a
    /// plain press (e.g. a fixed chord like Ctrl+Alt+Space) simply never
    /// fires this.
    /// </summary>
    event Action? Cancelled;

    bool Register();
    void Unregister();
}

/// <summary>Types text into whatever application is currently active.</summary>
public interface ITextInjector
{
    bool Inject(string text);
}

public interface ISoundPlayer
{
    void PlayStart();
    void PlayStop();
}

/// <summary>Runs speech-to-text on captured audio. <see cref="WhisperEngine"/> implements it.</summary>
public interface ITranscriber
{
    Task<TranscriptionResult> TranscribeAsync(float[] samples16k, string languageCode, CancellationToken ct);
}

/// <summary>
/// The only port allowed to hop threads. Everything else in
/// <see cref="DictationController"/> runs on a single thread/synchronization
/// context by contract: <see cref="RunAsync"/> runs its work off-thread — that
/// work must never call back into the controller directly, since there is no
/// synchronization context to marshal it onto a real scheduler's Task.Run
/// continuation; its returned <see cref="Task"/> is only for fire-and-forget
/// or awaiting completion. <see cref="Post"/> is the one sanctioned way for
/// that off-thread work to deliver a result back, and <see cref="Schedule"/>
/// invokes its action on the same context after the delay.
/// </summary>
public interface IScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);

    Task RunAsync(Func<Task> work);

    /// <summary>Runs <paramref name="action"/> on the controller's thread/context. The only sanctioned way for off-thread work to touch the controller.</summary>
    void Post(Action action);
}
