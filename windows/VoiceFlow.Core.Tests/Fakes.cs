using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public sealed class FakeAudioCapture : IAudioCapture
{
    public bool IsStarted { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public Exception? ThrowOnStart { get; set; }
    public float[] SamplesToReturn { get; set; } = [];

    public void Start()
    {
        StartCallCount++;
        if (ThrowOnStart is not null)
        {
            throw ThrowOnStart;
        }

        IsStarted = true;
    }

    public float[] Stop()
    {
        StopCallCount++;
        IsStarted = false;
        return SamplesToReturn;
    }
}

public sealed class FakeHotkey : IHotkey
{
    public event Action? Pressed;
    public event Action? Released;

    public bool RegisterResult { get; set; } = true;
    public bool IsRegistered { get; private set; }
    public int UnregisterCallCount { get; private set; }

    public bool Register()
    {
        IsRegistered = RegisterResult;
        return RegisterResult;
    }

    public void Unregister()
    {
        UnregisterCallCount++;
        IsRegistered = false;
    }

    public void RaisePressed() => Pressed?.Invoke();

    public void RaiseReleased() => Released?.Invoke();
}

public sealed class FakeTextInjector : ITextInjector
{
    public List<string> Injected { get; } = [];
    public bool InjectResult { get; set; } = true;

    public bool Inject(string text)
    {
        Injected.Add(text);
        return InjectResult;
    }
}

public sealed class FakeSoundPlayer : ISoundPlayer
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    public void PlayStart() => StartCount++;

    public void PlayStop() => StopCount++;
}

/// <summary>
/// Resolves transcriptions on demand via <see cref="Complete"/>/<see cref="Fail"/>
/// instead of actually running inference, so tests can control exactly when a
/// transcription "arrives" relative to a timeout.
/// </summary>
public sealed class FakeTranscriber : ITranscriber
{
    private TaskCompletionSource<TranscriptionResult>? pending;

    public int CallCount { get; private set; }
    public float[]? LastSamples { get; private set; }
    public string? LastLanguageCode { get; private set; }

    public Task<TranscriptionResult> TranscribeAsync(float[] samples16k, string languageCode, CancellationToken ct)
    {
        CallCount++;
        LastSamples = samples16k;
        LastLanguageCode = languageCode;
        pending = new TaskCompletionSource<TranscriptionResult>();
        return pending.Task;
    }

    public void Complete(string text) =>
        (pending ?? throw new InvalidOperationException("TranscribeAsync was not called.")).SetResult(new TranscriptionResult(text, TimeSpan.Zero));

    public void Fail(Exception ex) =>
        (pending ?? throw new InvalidOperationException("TranscribeAsync was not called.")).SetException(ex);
}

/// <summary>
/// Runs <see cref="RunAsync"/> work synchronously (up to its first await) and
/// keeps <see cref="Schedule"/>d actions pending until <see cref="FireTimeout"/>
/// fires the oldest one, or the caller disposes the handle — so tests are
/// deterministic without <c>Task.Delay</c> or real threads. <see cref="Post"/>
/// actions are queued rather than run inline, so a test can assert nothing
/// happened yet, then call <see cref="DrainPosted"/> to prove the marshalling
/// is what actually delivers the result — this is what a real scheduler's
/// <c>Task.Run</c> would otherwise deliver from an arbitrary thread-pool thread.
/// </summary>
public sealed class FakeScheduler : IScheduler
{
    private readonly List<Action> pending = [];

    public int PendingCount => pending.Count;

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        pending.Add(action);
        return new Cancellation(() => pending.Remove(action));
    }

    public Task? LastRunTask { get; private set; }

    public Task RunAsync(Func<Task> work) => LastRunTask = work();

    public Queue<Action> Posted { get; } = new();

    public void Post(Action action) => Posted.Enqueue(action);

    /// <summary>Runs every action queued by <see cref="Post"/> so far, in order.</summary>
    public void DrainPosted()
    {
        while (Posted.Count > 0)
        {
            Posted.Dequeue()();
        }
    }

    public void FireTimeout()
    {
        var action = pending[0];
        pending.RemoveAt(0);
        action();
    }

    private sealed class Cancellation(Action onDispose) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            onDispose();
        }
    }
}
