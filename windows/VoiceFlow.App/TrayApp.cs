using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Composition root: builds the adapters, the <see cref="DictationController"/>
/// and the model lifecycle, and owns the tray icon, the settings window and
/// every dialog. Mirrors <c>StatusBarController</c> on the Mac, including its
/// nesting-safe dialog guard (R23: an error that arrives while a dialog is
/// already up only updates the icon, and is shown once the dialog stack
/// unwinds — see <see cref="HandleError"/>/<see cref="PresentErrorDialog"/>)
/// and its "idle only if the engine loaded" rule after a download
/// (<see cref="ModelLifecycle"/>'s <c>onEngineLoaded</c> callback below).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayApp : ApplicationContext
{
    private readonly SettingsStore settingsStore;
    private readonly ModelLifecycle modelLifecycle;
    private readonly PushToTalkBackend hotkey;
    private readonly DictationController controller;
    private readonly NotifyIcon notifyIcon;

    private Settings settings;
    private SettingsForm? settingsForm;

    // NSAlert.runModal() pumps the run loop on the Mac, so a hotkey press (or
    // a download completion) can arrive while a dialog is up and try to raise
    // another one; without this guard that would stack a second dialog on top
    // of the first. Saved/restored around each dialog rather than just set
    // true/false, so a dialog opened from inside another dialog's button
    // handler (Download -> the progress dialog) still leaves the outer
    // dialog's "presenting" state correct once the inner one closes.
    private bool isPresentingDialog;

    // An error that arrived while isPresentingDialog was already true was not
    // shown; remembered here so it can be shown once PresentErrorDialog
    // finishes unwinding back to the top-level call (mirrors the Mac's
    // startModelDownload() re-checking `state` after its own alert closes).
    private string? pendingErrorCode;

    public TrayApp()
    {
        // WinForms installs its SynchronizationContext as part of
        // Application.Run's message loop; that has already happened by the
        // time Program.cs constructs this ApplicationContext, but guard
        // anyway so ThreadScheduler never captures a null context.
        if (SynchronizationContext.Current is null)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }

        settingsStore = new SettingsStore(WindowsPaths.SettingsFile);
        settings = settingsStore.Load();

        DiagnosticLog.Write(
            $"startup {VoiceFlowCore.Version} model={ModelCatalogue.For(settings.Model).Name} " +
            $"pushToTalkKey={settings.PushToTalkKey} language={settings.Language.WhisperCode()} " +
            $"cpu={Environment.ProcessorCount} threads");

        var scheduler = new ThreadScheduler();

        var audioCapture = new WasapiAudioCapture();
        hotkey = new PushToTalkBackend(() => settings.PushToTalkKey);
        var textInjector = new SendInputTextInjector();
        var soundPlayer = new SystemSoundPlayer();

        // One long-lived ModelManager for the app's whole lifetime; its
        // HttpClient is intentionally never disposed.
        var modelManager = new ModelManager(WindowsPaths.ModelsDirectory);
        modelLifecycle = new ModelLifecycle(
            modelManager,
            () => settings,
            HandleError,
            onEngineLoaded: () => SafeInvoke(() => UpdateIcon(DictationState.Idle)));

        controller = new DictationController(
            audioCapture,
            hotkey,
            textInjector,
            soundPlayer,
            scheduler,
            () => settings,
            () => modelLifecycle.Engine);
        controller.StateChanged += OnStateChanged;
        controller.ErrorRaised += HandleError;
        controller.Diagnostic += LogDiagnostic;

        notifyIcon = new NotifyIcon
        {
            Icon = TrayIcons.For(DictationState.Idle),
            Text = "VoiceFlow",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        notifyIcon.MouseClick += OnTrayIconClick;

        modelLifecycle.LoadModelIfPresent();

        // A conflict here already reaches the user: Start() raises
        // "hotkey-conflict" through controller.ErrorRaised, which
        // HandleError/PresentErrorDialog turns into a dialog. Either way the
        // app keeps running — the user can still quit from the tray.
        controller.Start();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => SafeInvoke(ShowSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => SafeInvoke(Quit));
        return menu;
    }

    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            SafeInvoke(ShowSettings);
        }
    }

    private void ShowSettings()
    {
        // Reused for the app's lifetime rather than recreated per click:
        // SettingsForm hides itself instead of closing when the user clicks
        // its own X, so this instance stays valid.
        settingsForm ??= new SettingsForm(
            settingsStore,
            getSettings: () => settings,
            setSettings: updated => settings = updated,
            onModelChanged: () => SafeInvoke(() => modelLifecycle.LoadModelIfPresent()),
            onPushToTalkKeyChanged: () => SafeInvoke(SwitchPushToTalkKey),
            onQuit: Quit);

        if (!settingsForm.Visible)
        {
            settingsForm.Show();
        }

        settingsForm.Activate();
        settingsForm.BringToFront();
    }

    /// <summary>
    /// Re-registers the hotkey backend after the user picks a different
    /// push-to-talk key in Settings — no relaunch needed. <c>Stop()</c>
    /// unregisters whichever backend was active (finishing an in-flight
    /// dictation via Released first, the same rule Quit and every other
    /// teardown path follows); <c>Start()</c> registers the newly selected
    /// one and, on conflict, raises "hotkey-conflict" through
    /// <see cref="DictationController.ErrorRaised"/> exactly like the
    /// startup path does — no separate dialog path.
    /// </summary>
    private void SwitchPushToTalkKey()
    {
        controller.Stop();
        controller.Start();
    }

    private void OnStateChanged(DictationState state) => SafeInvoke(() => UpdateIcon(state));

    private void UpdateIcon(DictationState state) => notifyIcon.Icon = TrayIcons.For(state);

    /// <summary>
    /// Turns one <see cref="DictationController.Diagnostic"/> event into a
    /// single log line, e.g. <c>event transcribed duration=812ms chars=42</c>
    /// — only the fields the event actually carries are included, and never
    /// the dictated text itself (only its length).
    /// </summary>
    private static void LogDiagnostic(DictationDiagnostic diagnostic)
    {
        var parts = new List<string> { $"event {diagnostic.Event}" };

        if (diagnostic.Duration is { } duration)
        {
            parts.Add($"duration={duration.TotalMilliseconds:0}ms");
        }

        if (diagnostic.Samples is { } samples)
        {
            parts.Add($"samples={samples} ({samples / 16000.0:0.0}s)");
        }

        if (diagnostic.Characters is { } characters)
        {
            parts.Add($"chars={characters}");
        }

        DiagnosticLog.Write(string.Join(' ', parts));
    }

    /// <summary>
    /// Handles every error the app can raise, whether from the dictation
    /// state machine (<see cref="DictationController.ErrorRaised"/>) or from
    /// the model lifecycle (missing/unloadable/undownloadable model). The
    /// icon always reflects the error; the dialog itself is deferred if one
    /// is already on screen.
    /// </summary>
    private void HandleError(string code) => SafeInvoke(() => HandleErrorCore(code));

    private void HandleErrorCore(string code)
    {
        DiagnosticLog.Error(code, controller.LastErrorException);
        UpdateIcon(DictationState.Error);

        if (isPresentingDialog)
        {
            pendingErrorCode = code;
            return;
        }

        PresentErrorDialog(code);
    }

    /// <summary>
    /// Runs a message-loop-invoked callback (a WndProc-driven event, a
    /// ToolStripItem click, a posted continuation) under a catch-all: WinForms
    /// terminates the whole process on an unhandled exception escaping such a
    /// callback, so a bug in a dialog or in the model lifecycle must never be
    /// allowed to propagate past this boundary.
    /// </summary>
    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowUnexpectedError(ex);
        }
    }

    private void ShowUnexpectedError(Exception ex)
    {
        if (isPresentingDialog)
        {
            // Best effort: honor the same one-dialog-at-a-time rule as
            // HandleErrorCore rather than stacking a second dialog. There is
            // no pendingErrorCode-style re-presentation for this path — an
            // unexpected exception here is already a bug, not a state the
            // rest of the app is waiting to hear about.
            return;
        }

        isPresentingDialog = true;
        try
        {
            try
            {
                ErrorDialogs.ShowUnexpected(settingsForm, ex);
            }
            catch
            {
                // This is the last-resort dialog; if showing it throws (e.g.
                // settingsForm was already disposed during Quit), there is no
                // further fallback, so swallow rather than take down the process.
            }
        }
        finally
        {
            isPresentingDialog = false;
        }
    }

    private void PresentErrorDialog(string code)
    {
        bool wasPresenting = isPresentingDialog;
        isPresentingDialog = true;
        pendingErrorCode = null;
        try
        {
            ErrorDialogs.Show(code, settingsForm, RetryActionFor(code), PushToTalkKeyLabel(settings.PushToTalkKey));
        }
        finally
        {
            isPresentingDialog = wasPresenting;
        }

        // A nested error was recorded but suppressed above while this dialog
        // (or one it triggered, e.g. the download progress form) was on
        // screen. Show it now that the dialog stack has unwound back to the
        // top-level call — never silently drop an error.
        if (!isPresentingDialog && pendingErrorCode is { } suppressed)
        {
            pendingErrorCode = null;
            PresentErrorDialog(suppressed);
        }
    }

    private Action? RetryActionFor(string code) => code switch
    {
        "model-missing" or "model-load-failed" => () => modelLifecycle.StartDownload(settingsForm),
        _ => null,
    };

    /// <summary>The label named in the "hotkey-conflict" dialog — the key that actually failed to register, read fresh from settings (it may have just changed, mid-switch).</summary>
    private static string PushToTalkKeyLabel(PushToTalkKey key) => key switch
    {
        PushToTalkKey.Ctrl => "Ctrl",
        PushToTalkKey.CtrlAltSpace => "Ctrl+Alt+Space",
        _ => "the configured push-to-talk key",
    };

    private void Quit()
    {
        // Cleanup runs in try/catch so a throwing Dispose() (e.g. from
        // ModelLifecycle, which calls into the native WhisperEngine) can't
        // leave the tray icon stuck and the process un-exitable; ExitThread()
        // always runs via the finally, so quitting is never blocked by it.
        try
        {
            controller.Stop();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            modelLifecycle.Dispose();
            hotkey.Dispose();
            settingsForm?.Dispose();
        }
        catch (Exception ex)
        {
            ShowUnexpectedError(ex);
        }
        finally
        {
            ExitThread();
        }
    }
}
