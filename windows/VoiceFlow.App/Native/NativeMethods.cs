using System.Runtime.InteropServices;

namespace VoiceFlow.App.Native;

/// <summary>Raw P/Invoke declarations for the Win32 hotkey and input-injection APIs. No behavior here — see <see cref="VoiceFlow.App.CtrlAltSpaceHotkey"/>, <see cref="VoiceFlow.App.CtrlKeyMonitor"/> and <see cref="VoiceFlow.App.SendInputTextInjector"/>.</summary>
internal static class NativeMethods
{
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int VK_SPACE = 0x20;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;

    internal const int WM_HOTKEY = 0x0312;

    // WH_KEYBOARD_LL messages delivered to the hook procedure via wParam.
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;

    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const uint INPUT_KEYBOARD = 1;

    internal const int WH_KEYBOARD_LL = 13;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // Frees the temporary GDI icon handle Bitmap.GetHicon() hands back — see
    // VoiceFlow.App.TrayIcons, which clones it into an owned Icon and then
    // destroys the raw handle immediately.
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // The WH_KEYBOARD_LL callback signature. Kept as a delegate field on the
    // installing class (VoiceFlow.App.CtrlKeyMonitor) rather than a local, so
    // the GC cannot collect it while the hook is installed — a native
    // callback pointing at a collected delegate is undefined behavior.
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);
}

// The struct pointed to by lParam in the WH_KEYBOARD_LL callback.
[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

// The three payload shapes (keyboard/mouse/hardware) overlap the same bytes,
// exactly like the native INPUT union — LayoutKind.Explicit with a shared
// FieldOffset is the marshaling equivalent of a C union.
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public MOUSEINPUT mi;

    [FieldOffset(0)]
    public KEYBDINPUT ki;

    [FieldOffset(0)]
    public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}
