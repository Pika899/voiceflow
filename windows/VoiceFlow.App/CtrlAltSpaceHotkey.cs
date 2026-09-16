using System.Runtime.Versioning;
using VoiceFlow.App.Native;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Push-to-talk backend using the global hotkey Control+Alt+Space, delivered
/// through <c>RegisterHotKey</c>/<c>WM_HOTKEY</c> on a hidden message-only
/// window. Mirrors <c>HotkeyManager.swift</c>'s contract: <see cref="Register"/>
/// returns false on conflict, and every press eventually gets exactly one
/// <see cref="Released"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkey : NativeWindow, IHotkey, IDisposable
{
    private const int HotkeyId = 1;

    // WM_HOTKEY fires only on key-down; Win32 has no matching key-up message
    // for it, so a release is detected by polling GetAsyncKeyState on the
    // combination's keys until all of them are up.
    private readonly System.Windows.Forms.Timer releasePollTimer = new() { Interval = 16 };

    private bool isRegistered;
    private bool isDown;

    public event Action? Pressed;
    public event Action? Released;

    public GlobalHotkey()
    {
        // Parent = HWND_MESSAGE (-3): a message-only window has no visible
        // surface, never appears in the taskbar or z-order, and only needs
        // to exist to receive WM_HOTKEY on its handle.
        var createParams = new CreateParams { Parent = new IntPtr(-3) };
        CreateHandle(createParams);

        releasePollTimer.Tick += OnReleasePollTick;
    }

    public bool Register()
    {
        if (isRegistered)
        {
            return true;
        }

        isRegistered = NativeMethods.RegisterHotKey(
            Handle,
            HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_SPACE);
        return isRegistered;
    }

    public void Unregister()
    {
        releasePollTimer.Stop();

        if (isRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            isRegistered = false;
        }

        // Mirrors the Mac's finishDictation-before-teardown rule: if the key
        // was down when we tore down, the controller would otherwise stay in
        // Listening forever waiting for a Released that can no longer arrive.
        if (isDown)
        {
            isDown = false;
            Released?.Invoke();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            OnHotkeyMessage();
            return;
        }

        base.WndProc(ref m);
    }

    private void OnHotkeyMessage()
    {
        if (isDown)
        {
            return;
        }

        isDown = true;
        Pressed?.Invoke();
        releasePollTimer.Start();
    }

    private void OnReleasePollTick(object? sender, EventArgs e)
    {
        bool chordFullyHeld = IsKeyDown(NativeMethods.VK_SPACE)
            && IsKeyDown(NativeMethods.VK_CONTROL)
            && IsKeyDown(NativeMethods.VK_MENU);
        if (chordFullyHeld)
        {
            return;
        }

        // Chord no longer held; mirrors the Mac hotkey release semantics
        // (Carbon's kEventHotKeyReleased fires the instant any key of the
        // combination lifts, not only once every key is up).
        releasePollTimer.Stop();
        if (isDown)
        {
            isDown = false;
            Released?.Invoke();
        }
    }

    private static bool IsKeyDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        Unregister();
        releasePollTimer.Tick -= OnReleasePollTick;
        releasePollTimer.Dispose();
        DestroyHandle();
    }
}
