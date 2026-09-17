using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VoiceFlow.App.Native;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Push-to-talk backend for the bare Ctrl key, held alone (ruling R-W4,
/// default). Ctrl is a modifier, not a key <c>RegisterHotKey</c> can bind
/// (Win32 hotkeys need at least one non-modifier key), so this uses a
/// low-level keyboard hook instead — the Win32 analogue of the Mac's
/// <c>.flagsChanged</c> monitor for the fn key. One of the two
/// <see cref="PushToTalkBackend"/> options alongside <see cref="CtrlAltSpaceHotkey"/>.
///
/// The hook callback runs synchronously on the thread that installed the
/// hook (<see cref="Register"/> must therefore be called from the WinForms
/// UI thread), so every event this class raises already satisfies the
/// single-thread contract <see cref="VoiceFlow.Core.IHotkey"/> implementations
/// must honor — no marshalling needed here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CtrlKeyMonitor : IHotkey, IDisposable
{
    // Kept in a field, not a local or a method group passed straight to
    // SetWindowsHookEx, so the GC cannot collect the delegate while the hook
    // is installed — the classic SetWindowsHookEx use-after-free bug.
    private readonly NativeMethods.LowLevelKeyboardProc hookProc;

    private IntPtr hookHandle = IntPtr.Zero;

    // held: a Ctrl key is currently down and Pressed has fired for it.
    // cancelled: while held, some other key went down first, so the eventual
    // Ctrl-up must not fire Released (Cancelled already fired instead).
    private bool held;
    private bool cancelled;

    public event Action? Pressed;
    public event Action? Released;
    public event Action? Cancelled;

    public CtrlKeyMonitor()
    {
        hookProc = HookCallback;
    }

    public bool Register()
    {
        if (hookHandle != IntPtr.Zero)
        {
            return true;
        }

        hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            hookProc,
            NativeMethods.GetModuleHandle(null),
            0);
        return hookHandle != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }

        // Mirrors CtrlAltSpaceHotkey.Unregister: if Ctrl was down when we
        // tore down, fire the event that would otherwise never arrive, so
        // the controller never stays stuck in Listening.
        if (held)
        {
            held = false;
            if (!cancelled)
            {
                Released?.Invoke();
            }

            cancelled = false;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // A negative nCode means the hook must not process the event at all,
        // only pass it on (standard WH_KEYBOARD_LL contract).
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            bool isCtrl = data.vkCode == NativeMethods.VK_LCONTROL || data.vkCode == NativeMethods.VK_RCONTROL;

            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                HandleKeyDown(isCtrl);
            }
            else if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                HandleKeyUp(isCtrl);
            }
        }

        // Never swallow the key: always pass it on, so Ctrl (and every other
        // key) keeps working normally for the rest of the system — VoiceFlow
        // only observes, it never intercepts.
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleKeyDown(bool isCtrl)
    {
        if (isCtrl)
        {
            if (held)
            {
                // Auto-repeat while held, or the second Ctrl key going down
                // while the first is already held: both ignored, since a
                // Pressed already fired for this press-cycle.
                return;
            }

            held = true;
            cancelled = false;
            Pressed?.Invoke();
            return;
        }

        if (held && !cancelled)
        {
            // Some other key went down while Ctrl was held: this was a
            // keyboard shortcut (Ctrl+C, an AltGr sequence delivered as
            // LCONTROL down + RMENU down, the Windows key, ...), not a
            // dictation. Cancel it, but keep passing every key through — the
            // shortcut itself must still reach the app that owns it.
            cancelled = true;
            Cancelled?.Invoke();
        }
    }

    private void HandleKeyUp(bool isCtrl)
    {
        if (!isCtrl || !held)
        {
            return;
        }

        // If both Ctrl keys were down, the first one to lift ends the press
        // (held becomes false here), so the second Ctrl-up later is just a
        // plain key release with nothing to do — mirrors the "any key of the
        // chord lifts" rule CtrlAltSpaceHotkey already implements.
        held = false;
        if (!cancelled)
        {
            Released?.Invoke();
        }

        cancelled = false;
    }

    public void Dispose() => Unregister();
}
