using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// The controller's <see cref="IScheduler"/> on Windows: marshals work onto
/// the WinForms UI thread via the <see cref="SynchronizationContext"/> that
/// <c>Application.Run</c> installs
/// (<c>WindowsFormsSynchronizationContext</c>), which is also the thread the
/// <see cref="DictationController"/> lives on by contract.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ThreadScheduler : IScheduler
{
    private readonly SynchronizationContext context;

    public ThreadScheduler()
    {
        // Captured at construction time, not lazily: a null context here
        // means this was built before Application set up the WinForms
        // context (or off the UI thread entirely), and every Post/Schedule
        // below would silently do the wrong thing instead of marshaling.
        context = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "ThreadScheduler must be constructed on the UI thread after Application has started.");
    }

    public void Post(Action action) => context.Post(_ => action(), null);

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new System.Windows.Forms.Timer
        {
            Interval = (int)Math.Max(1, delay.TotalMilliseconds),
        };
        timer.Tick += (_, _) =>
        {
            // One-shot: stop and dispose before invoking, so a re-entrant
            // Schedule call from inside `action` can't observe a still-live
            // timer for a delay that has already fired.
            timer.Stop();
            timer.Dispose();
            action();
        };
        timer.Start();
        return new TimerHandle(timer);
    }

    public Task RunAsync(Func<Task> work) => Task.Run(work);

    private sealed class TimerHandle(System.Windows.Forms.Timer timer) : IDisposable
    {
        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }
    }
}
