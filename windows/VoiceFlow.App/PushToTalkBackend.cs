using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Owns both push-to-talk backends (<see cref="CtrlKeyMonitor"/> and
/// <see cref="CtrlAltSpaceHotkey"/>) and forwards events from whichever one
/// is currently registered, so <see cref="VoiceFlow.Core.DictationController"/>
/// only ever talks to a single <see cref="IHotkey"/>.
///
/// Which backend is "current" is read fresh from <c>getKey</c> every time
/// <see cref="Register"/> runs, not cached at construction — that is what
/// lets switching the setting take effect without a relaunch: TrayApp's
/// Settings-form callback does <c>controller.Stop()</c> (which calls
/// <see cref="Unregister"/> on whichever backend was active) followed by
/// <c>controller.Start()</c> (which calls <see cref="Register"/> again, now
/// picking up the newly saved key).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PushToTalkBackend : IHotkey, IDisposable
{
    private readonly CtrlKeyMonitor ctrlMonitor = new();
    private readonly CtrlAltSpaceHotkey ctrlAltSpaceHotkey = new();
    private readonly Func<PushToTalkKey> getKey;

    private IHotkey? active;

    public event Action? Pressed;
    public event Action? Released;
    public event Action? Cancelled;

    public PushToTalkBackend(Func<PushToTalkKey> getKey)
    {
        this.getKey = getKey;

        ctrlMonitor.Pressed += OnPressed;
        ctrlMonitor.Released += OnReleased;
        ctrlMonitor.Cancelled += OnCancelled;

        ctrlAltSpaceHotkey.Pressed += OnPressed;
        ctrlAltSpaceHotkey.Released += OnReleased;
        // CtrlAltSpaceHotkey.Cancelled never fires (see its doc comment); no
        // subscription needed.
    }

    public bool Register()
    {
        var backend = BackendFor(getKey());
        if (!backend.Register())
        {
            return false;
        }

        active = backend;
        return true;
    }

    public void Unregister()
    {
        active?.Unregister();
        active = null;
    }

    private IHotkey BackendFor(PushToTalkKey key) => key switch
    {
        PushToTalkKey.Ctrl => ctrlMonitor,
        PushToTalkKey.CtrlAltSpace => ctrlAltSpaceHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private void OnPressed() => Pressed?.Invoke();

    private void OnReleased() => Released?.Invoke();

    private void OnCancelled() => Cancelled?.Invoke();

    public void Dispose()
    {
        Unregister();
        ctrlMonitor.Dispose();
        ctrlAltSpaceHotkey.Dispose();
    }
}
