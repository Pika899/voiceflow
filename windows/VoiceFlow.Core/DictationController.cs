namespace VoiceFlow.Core;

public enum DictationState
{
    Idle,
    Listening,
    Transcribing,
    Error,
}

/// <summary>
/// Push-to-talk state machine: press -&gt; capture audio -&gt; release -&gt;
/// transcribe -&gt; inject. Ports it against (<see cref="IAudioCapture"/>,
/// <see cref="IHotkey"/>, <see cref="ITextInjector"/>, <see cref="ISoundPlayer"/>,
/// <see cref="ITranscriber"/>) are UI-free and hardware-free, so the whole flow
/// is unit-testable.
///
/// Threading contract: every public entry point and every callback (hotkey
/// press/release, scheduled timeout, transcription completion) is expected to
/// run on the same thread/synchronization context. <see cref="IScheduler"/> is
/// the only port allowed to hop threads; it marshals its own work back onto
/// that context, so this class itself never needs locks.
/// </summary>
public sealed class DictationController
{
    private readonly IAudioCapture audio;
    private readonly IHotkey hotkey;
    private readonly ITextInjector injector;
    private readonly ISoundPlayer sounds;
    private readonly IScheduler scheduler;
    private readonly Func<Settings> settings;
    private readonly Func<ITranscriber?> transcriber;

    // Last character this app typed, for apps whose text isn't readable back:
    // consecutive dictations must still get a space between them (mirrors
    // StatusBarController.lastInjectedCharacter on macOS).
    private char? lastInjectedCharacter;

    // Identifies the current press/release cycle. A run that times out keeps
    // its id forever "spent" (see timedOutRunId below); a run superseded by a
    // later press/release cycle is identified by id mismatch. Together these
    // let a late transcription completion recognize it belongs to a run the
    // controller has already moved on from, even across multiple dictations
    // (R20: correct-by-construction, rather than a single shared flag that a
    // second run could accidentally reuse).
    private int runId;
    private int? timedOutRunId;
    private IDisposable? timeoutHandle;

    public DictationController(
        IAudioCapture audio,
        IHotkey hotkey,
        ITextInjector injector,
        ISoundPlayer sounds,
        IScheduler scheduler,
        Func<Settings> settings,
        Func<ITranscriber?> transcriber)
    {
        this.audio = audio;
        this.hotkey = hotkey;
        this.injector = injector;
        this.sounds = sounds;
        this.scheduler = scheduler;
        this.settings = settings;
        this.transcriber = transcriber;

        hotkey.Pressed += OnPressed;
        hotkey.Released += OnReleased;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public string? LastErrorCode { get; private set; }

    public event Action<DictationState>? StateChanged;

    /// <summary>Fires once per transition into <see cref="DictationState.Error"/>, with the error code.</summary>
    public event Action<string>? ErrorRaised;

    public static readonly TimeSpan TranscriptionTimeout = TimeSpan.FromSeconds(15);

    public bool Start()
    {
        if (hotkey.Register())
        {
            return true;
        }

        SetError("hotkey-conflict");
        return false;
    }

    public void Stop() => hotkey.Unregister();

    private void OnPressed()
    {
        // A press while the previous inference is still running must not
        // restart capture: the old completion would later race the new
        // recording (mirrors StatusBarController.beginDictation).
        if (State is DictationState.Listening or DictationState.Transcribing)
        {
            return;
        }

        if (transcriber() is null)
        {
            SetError("model-missing");
            return;
        }

        try
        {
            // Cue first: most of the start sound precedes the engine actually
            // opening the mic. The stop cue is played after Stop(), so it can
            // never reach the captured buffer.
            if (settings().PlaySounds)
            {
                sounds.PlayStart();
            }

            audio.Start();
            SetState(DictationState.Listening);
        }
        catch (AudioCaptureException ex)
        {
            SetError(ex.Code);
        }
        catch
        {
            SetError("audio-start-failed");
        }
    }

    private void OnReleased()
    {
        if (State != DictationState.Listening)
        {
            return;
        }

        var samples = audio.Stop();
        if (settings().PlaySounds)
        {
            sounds.PlayStop();
        }

        SetState(DictationState.Transcribing);

        var thisRun = ++runId;
        // Honest limitation (mirrors the Mac): the transcription itself is not
        // cancelled when the timeout fires. This only changes what the state
        // machine reports after 15s; the in-flight work keeps running and its
        // late result is dropped below rather than injected.
        timeoutHandle = scheduler.Schedule(TranscriptionTimeout, () => OnTimeout(thisRun));

        _ = scheduler.RunAsync(() => RunTranscriptionAsync(thisRun, samples));
    }

    private void OnTimeout(int forRun)
    {
        if (forRun != runId)
        {
            return; // superseded by a later run; nothing to do
        }

        timedOutRunId = forRun;
        SetError("transcription-timeout");
    }

    private async Task RunTranscriptionAsync(int forRun, float[] samples)
    {
        try
        {
            var result = await transcriber()!.TranscribeAsync(samples, settings().Language.WhisperCode(), CancellationToken.None)
                .ConfigureAwait(false);
            OnTranscribed(forRun, result);
        }
        catch
        {
            OnTranscriptionFailed(forRun);
        }
    }

    private void OnTranscribed(int forRun, TranscriptionResult result)
    {
        if (!IsCurrentRun(forRun))
        {
            return; // the UI already reported a timeout for this run; drop the late result
        }

        CancelTimeout();

        if (string.IsNullOrEmpty(result.Text))
        {
            SetState(DictationState.Idle);
            return;
        }

        CursorContext context = lastInjectedCharacter is char last
            ? new CursorContext.Character(last)
            : new CursorContext.Unavailable();
        var joined = TextJoiner.Prefix(result.Text, context) + result.Text;

        if (!injector.Inject(joined))
        {
            SetError("injection-failed");
            return;
        }

        lastInjectedCharacter = joined[^1];
        SetState(DictationState.Idle);
    }

    private void OnTranscriptionFailed(int forRun)
    {
        if (!IsCurrentRun(forRun))
        {
            return;
        }

        CancelTimeout();
        SetError("transcription-failed");
    }

    private bool IsCurrentRun(int forRun) => forRun == runId && timedOutRunId != forRun;

    private void CancelTimeout()
    {
        timeoutHandle?.Dispose();
        timeoutHandle = null;
    }

    private void SetState(DictationState newState)
    {
        State = newState;
        StateChanged?.Invoke(State);
    }

    private void SetError(string code)
    {
        LastErrorCode = code;
        State = DictationState.Error;
        StateChanged?.Invoke(State);
        ErrorRaised?.Invoke(code);
    }
}
