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
/// context by contract: <see cref="RunAsync"/> does the off-thread work and
/// marshals its continuation back, and <see cref="Schedule"/> invokes its
/// action back on that same context after the delay.
/// </summary>
public interface IScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);

    Task RunAsync(Func<Task> work);
}
