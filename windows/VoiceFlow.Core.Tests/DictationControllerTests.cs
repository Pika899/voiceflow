using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class DictationControllerTests
{
    private sealed class Harness
    {
        public FakeAudioCapture Audio { get; } = new();
        public FakeHotkey Hotkey { get; } = new();
        public FakeTextInjector Injector { get; } = new();
        public FakeSoundPlayer Sounds { get; } = new();
        public FakeScheduler Scheduler { get; } = new();
        public FakeTranscriber? Transcriber { get; set; } = new();
        public Settings Settings { get; set; } = new(PlaySounds: true, Language: DictationLanguage.Italian);
        public List<DictationState> States { get; } = [];
        public List<string> Errors { get; } = [];

        public DictationController Controller { get; }

        public Harness()
        {
            Controller = new DictationController(
                Audio,
                Hotkey,
                Injector,
                Sounds,
                Scheduler,
                () => Settings,
                () => Transcriber);
            Controller.StateChanged += States.Add;
            Controller.ErrorRaised += Errors.Add;
        }

        /// <summary>
        /// Drives a full press -&gt; release, leaving the transcription in
        /// flight, and returns the task the controller is running it under.
        /// </summary>
        public Task PressAndRelease()
        {
            Hotkey.RaisePressed();
            Hotkey.RaiseReleased();
            return Scheduler.LastRunTask!;
        }

        /// <summary>
        /// Resolves <paramref name="runTask"/>'s transcription and blocks
        /// until the controller has queued its result with the scheduler
        /// (<see cref="Scheduler"/>'s <c>Post</c>, not yet delivered). The
        /// fake scheduler runs the transcription inline up to its first
        /// await, so nothing but that final continuation is actually
        /// asynchronous here — this just makes the test wait for it instead
        /// of racing it.
        /// </summary>
        private static void Drain(Task runTask) => runTask.GetAwaiter().GetResult();

        /// <summary>Signals a successful transcription, but does not deliver it — see <see cref="Drain"/>.</summary>
        public void SignalCompletion(Task runTask, FakeTranscriber transcriber, string text)
        {
            transcriber.Complete(text);
            Drain(runTask);
        }

        /// <summary>Signals a failed transcription, but does not deliver it — see <see cref="Drain"/>.</summary>
        public void SignalFailure(Task runTask, FakeTranscriber transcriber, Exception ex)
        {
            transcriber.Fail(ex);
            Drain(runTask);
        }

        /// <summary>Signals and fully delivers a successful transcription (signal + the scheduler's Post drained).</summary>
        public void Complete(Task runTask, FakeTranscriber transcriber, string text)
        {
            SignalCompletion(runTask, transcriber, text);
            Scheduler.DrainPosted();
        }

        /// <summary>Signals and fully delivers a failed transcription (signal + the scheduler's Post drained).</summary>
        public void Fail(Task runTask, FakeTranscriber transcriber, Exception ex)
        {
            SignalFailure(runTask, transcriber, ex);
            Scheduler.DrainPosted();
        }
    }

    [Fact]
    public void InitialStateIsIdle()
    {
        var h = new Harness();

        Assert.Equal(DictationState.Idle, h.Controller.State);
        Assert.Null(h.Controller.LastErrorCode);
    }

    [Fact]
    public void PressStartsListening()
    {
        var h = new Harness();

        h.Hotkey.RaisePressed();

        Assert.Equal(DictationState.Listening, h.Controller.State);
        Assert.True(h.Audio.IsStarted);
        Assert.Equal(1, h.Sounds.StartCount);
        Assert.Equal([DictationState.Listening], h.States);
    }

    [Fact]
    public void PressWhileListeningIsIgnored()
    {
        var h = new Harness();
        h.Hotkey.RaisePressed();

        h.Hotkey.RaisePressed();

        Assert.Equal(1, h.Audio.StartCallCount);
        Assert.Equal([DictationState.Listening], h.States);
    }

    [Fact]
    public void PressWhileTranscribingIsIgnored()
    {
        var h = new Harness();
        h.PressAndRelease();
        Assert.Equal(DictationState.Transcribing, h.Controller.State);

        h.Hotkey.RaisePressed();

        Assert.Equal(1, h.Audio.StartCallCount);
        Assert.Equal(DictationState.Transcribing, h.Controller.State);
    }

    [Fact]
    public void PressWithNoTranscriberRaisesModelMissingWithoutStartingAudio()
    {
        var h = new Harness { Transcriber = null };

        h.Hotkey.RaisePressed();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("model-missing", h.Controller.LastErrorCode);
        Assert.Equal(0, h.Audio.StartCallCount);
        Assert.Equal(0, h.Sounds.StartCount);
        Assert.Equal(["model-missing"], h.Errors);
    }

    [Fact]
    public void PressPropagatesAudioCaptureExceptionCode()
    {
        var h = new Harness();
        h.Audio.ThrowOnStart = new AudioCaptureException("microphone-permission", "no mic access");

        h.Hotkey.RaisePressed();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("microphone-permission", h.Controller.LastErrorCode);
        // The start cue plays before audio.Start() is attempted, so it still fires.
        Assert.Equal(1, h.Sounds.StartCount);
    }

    [Fact]
    public void PressWithUnexpectedAudioExceptionRaisesAudioStartFailed()
    {
        var h = new Harness();
        h.Audio.ThrowOnStart = new InvalidOperationException("device busy");

        h.Hotkey.RaisePressed();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("audio-start-failed", h.Controller.LastErrorCode);
    }

    [Fact]
    public void PressAfterErrorRecoversToListening()
    {
        var h = new Harness();
        h.Audio.ThrowOnStart = new InvalidOperationException("device busy");
        h.Hotkey.RaisePressed();
        Assert.Equal(DictationState.Error, h.Controller.State);
        h.Audio.ThrowOnStart = null;

        h.Hotkey.RaisePressed();

        Assert.Equal(DictationState.Listening, h.Controller.State);
    }

    [Fact]
    public void SoundsNotPlayedWhenPlaySoundsIsFalse()
    {
        var h = new Harness { Settings = new Settings(PlaySounds: false) };

        h.Hotkey.RaisePressed();
        h.Hotkey.RaiseReleased();

        Assert.Equal(0, h.Sounds.StartCount);
        Assert.Equal(0, h.Sounds.StopCount);
    }

    [Fact]
    public void ReleaseWhileNotListeningIsIgnored()
    {
        var h = new Harness();

        h.Hotkey.RaiseReleased();

        Assert.Equal(DictationState.Idle, h.Controller.State);
        Assert.Equal(0, h.Audio.StopCallCount);
        Assert.Empty(h.States);
    }

    [Fact]
    public void ReleaseStopsAudioPlaysStopCueAndStartsTranscribing()
    {
        var h = new Harness();
        h.Audio.SamplesToReturn = [1f, 2f, 3f];
        h.Hotkey.RaisePressed();

        h.Hotkey.RaiseReleased();

        Assert.Equal(1, h.Audio.StopCallCount);
        Assert.Equal(1, h.Sounds.StopCount);
        Assert.Equal(DictationState.Transcribing, h.Controller.State);
        Assert.Equal(1, h.Transcriber!.CallCount);
        Assert.Same(h.Audio.SamplesToReturn, h.Transcriber.LastSamples);
        Assert.Equal("it", h.Transcriber.LastLanguageCode);
    }

    [Fact]
    public void ReleaseWithNoTranscriberRaisesModelMissingWithoutSchedulingTranscription()
    {
        // The model can become unavailable while a dictation was in flight;
        // finishDictation()'s guard on the Mac is re-checked here too, not
        // just at press time.
        var h = new Harness();
        h.Hotkey.RaisePressed();
        Assert.Equal(DictationState.Listening, h.Controller.State);
        h.Transcriber = null;

        h.Hotkey.RaiseReleased();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("model-missing", h.Controller.LastErrorCode);
        Assert.Equal(1, h.Audio.StopCallCount);
        Assert.Equal(1, h.Sounds.StopCount);
        Assert.Equal(0, h.Scheduler.PendingCount); // no timeout scheduled
        Assert.Null(h.Scheduler.LastRunTask); // no transcription started
    }

    [Fact]
    public void SuccessfulResultIsMarshalledThroughPostNotDeliveredInline()
    {
        // Proves the single-thread contract: signaling the transcriber and
        // draining its Task only gets the result as far as the scheduler's
        // Post queue. Nothing that touches controller state may run before
        // the scheduler actually delivers it.
        var h = new Harness();
        var run = h.PressAndRelease();

        h.SignalCompletion(run, h.Transcriber!, "Ciao");

        Assert.Equal(DictationState.Transcribing, h.Controller.State);
        Assert.Empty(h.Injector.Injected);

        h.Scheduler.DrainPosted();

        Assert.Equal(DictationState.Idle, h.Controller.State);
        Assert.Equal(["Ciao"], h.Injector.Injected);
    }

    [Fact]
    public void FailedTranscriptionIsMarshalledThroughPostNotDeliveredInline()
    {
        var h = new Harness();
        var run = h.PressAndRelease();

        h.SignalFailure(run, h.Transcriber!, new InvalidOperationException("boom"));

        Assert.Equal(DictationState.Transcribing, h.Controller.State);
        Assert.Null(h.Controller.LastErrorCode);

        h.Scheduler.DrainPosted();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-failed", h.Controller.LastErrorCode);
    }

    [Fact]
    public void TimeoutBeforeDrainingAPostedResultStillDropsIt()
    {
        // The transcription can finish (and get as far as the scheduler's
        // Post queue) before the timeout fires but be delivered only after —
        // the run id/timedOutRunId bookkeeping must drop it regardless of
        // when Post is drained relative to the timeout.
        var h = new Harness();
        var run = h.PressAndRelease();
        h.SignalCompletion(run, h.Transcriber!, "Ciao"); // ready, not yet delivered

        h.Scheduler.FireTimeout(); // timeout wins the race first

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);

        h.Scheduler.DrainPosted(); // the late result is delivered now

        Assert.Empty(h.Injector.Injected);
        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);
    }

    [Fact]
    public void EmptyTranscriptionGoesIdleWithoutInjecting()
    {
        var h = new Harness();
        var run = h.PressAndRelease();

        h.Complete(run, h.Transcriber!, "");

        Assert.Equal(DictationState.Idle, h.Controller.State);
        Assert.Empty(h.Injector.Injected);
    }

    [Fact]
    public void FirstDictationInjectsWithoutLeadingSpace()
    {
        var h = new Harness();
        var run = h.PressAndRelease();

        h.Complete(run, h.Transcriber!, "Ciao");

        Assert.Equal(["Ciao"], h.Injector.Injected);
        Assert.Equal(DictationState.Idle, h.Controller.State);
    }

    [Fact]
    public void SecondConsecutiveDictationGetsLeadingSpace()
    {
        var h = new Harness();
        var firstRun = h.PressAndRelease();
        h.Complete(firstRun, h.Transcriber!, "siamo!");
        Assert.Equal(["siamo!"], h.Injector.Injected);

        var secondRun = h.PressAndRelease();
        h.Complete(secondRun, h.Transcriber!, "Adesso");

        Assert.Equal(["siamo!", " Adesso"], h.Injector.Injected);
    }

    [Fact]
    public void InjectionFailureRaisesInjectionFailedAndDoesNotRememberLastCharacter()
    {
        var h = new Harness();
        var firstRun = h.PressAndRelease();
        h.Complete(firstRun, h.Transcriber!, "siamo!");
        h.Injector.InjectResult = false;

        var secondRun = h.PressAndRelease();
        h.Complete(secondRun, h.Transcriber!, "Adesso");

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("injection-failed", h.Controller.LastErrorCode);

        // The failed injection did not update the remembered character, so a
        // subsequent successful dictation still sees '!' as context.
        h.Injector.InjectResult = true;
        var thirdRun = h.PressAndRelease();
        h.Complete(thirdRun, h.Transcriber!, "Ok");
        Assert.Equal(" Ok", h.Injector.Injected[^1]);
    }

    [Fact]
    public void TranscriptionExceptionRaisesTranscriptionFailed()
    {
        var h = new Harness();
        var run = h.PressAndRelease();

        h.Fail(run, h.Transcriber!, new InvalidOperationException("boom"));

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-failed", h.Controller.LastErrorCode);
        Assert.Empty(h.Injector.Injected);
    }

    [Fact]
    public void SuccessfulCompletionCancelsThePendingTimeout()
    {
        var h = new Harness();
        var run = h.PressAndRelease();
        Assert.Equal(1, h.Scheduler.PendingCount);

        h.Complete(run, h.Transcriber!, "Ciao");

        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void TimeoutWhileTranscribingRaisesTranscriptionTimeout()
    {
        var h = new Harness();
        h.PressAndRelease();

        h.Scheduler.FireTimeout();

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);
    }

    [Fact]
    public void LateSuccessfulResultAfterTimeoutIsDroppedSilently()
    {
        var h = new Harness();
        var run = h.PressAndRelease();
        h.Scheduler.FireTimeout();
        var statesAfterTimeout = h.States.Count;
        var errorsAfterTimeout = h.Errors.Count;

        h.Complete(run, h.Transcriber!, "Ciao");

        Assert.Empty(h.Injector.Injected);
        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);
        Assert.Equal(statesAfterTimeout, h.States.Count);
        Assert.Equal(errorsAfterTimeout, h.Errors.Count);
    }

    [Fact]
    public void LateFailureAfterTimeoutIsDroppedSilently()
    {
        var h = new Harness();
        var run = h.PressAndRelease();
        h.Scheduler.FireTimeout();
        var statesAfterTimeout = h.States.Count;
        var errorsAfterTimeout = h.Errors.Count;

        h.Fail(run, h.Transcriber!, new InvalidOperationException("late boom"));

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);
        Assert.Equal(statesAfterTimeout, h.States.Count);
        Assert.Equal(errorsAfterTimeout, h.Errors.Count);
    }

    [Fact]
    public void ErrorRaisedFiresExactlyOncePerErrorTransition()
    {
        var h = new Harness { Transcriber = null };

        h.Hotkey.RaisePressed(); // model-missing, 1st transition into Error
        h.Hotkey.RaisePressed(); // press while already in Error, no transcriber: a 2nd transition

        Assert.Equal(["model-missing", "model-missing"], h.Errors);
        Assert.Equal([DictationState.Error, DictationState.Error], h.States);
    }

    [Fact]
    public void ATimedOutRunDoesNotSuppressTheNextRunsLateFailure()
    {
        // A stale completion from a superseded run must not be confused with
        // the current run's own completion (correct-by-construction per R20):
        // give each dictation its own run id rather than a single shared flag.
        var h = new Harness();
        var firstRunTranscriber = h.Transcriber!;
        var firstRun = h.PressAndRelease();
        h.Scheduler.FireTimeout();
        Assert.Equal("transcription-timeout", h.Controller.LastErrorCode);

        // A fresh transcriber for the second run: the first run's own fake is
        // still "in flight" and must stay resolvable independently.
        h.Transcriber = new FakeTranscriber();
        var secondRunTranscriber = h.Transcriber;
        var secondRun = h.PressAndRelease(); // starts a brand-new run while the first is still "in flight"

        h.Fail(secondRun, secondRunTranscriber, new InvalidOperationException("second run failed"));

        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("transcription-failed", h.Controller.LastErrorCode);

        // The first run's late completion, arriving even later, is still dropped.
        h.Complete(firstRun, firstRunTranscriber, "late text from run 1");
        Assert.Empty(h.Injector.Injected);
    }

    [Fact]
    public void StartRegistersTheHotkey()
    {
        var h = new Harness();

        var result = h.Controller.Start();

        Assert.True(result);
        Assert.True(h.Hotkey.IsRegistered);
    }

    [Fact]
    public void StartReturnsFalseAndRaisesHotkeyConflict()
    {
        var h = new Harness();
        h.Hotkey.RegisterResult = false;

        var result = h.Controller.Start();

        Assert.False(result);
        Assert.Equal(DictationState.Error, h.Controller.State);
        Assert.Equal("hotkey-conflict", h.Controller.LastErrorCode);
    }

    [Fact]
    public void StopUnregistersTheHotkey()
    {
        var h = new Harness();
        h.Controller.Start();

        h.Controller.Stop();

        Assert.Equal(1, h.Hotkey.UnregisterCallCount);
    }
}
