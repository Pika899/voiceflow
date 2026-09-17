using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VoiceFlow.App.Native;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Types Unicode text into whichever window currently has focus, via
/// synthetic <c>SendInput</c> keyboard events. Mirrors
/// <c>TextInjector.insertViaSyntheticKeystrokes</c>: one down/up pair per
/// UTF-16 code unit, and any failure to deliver every event is reported back
/// as <c>false</c> rather than assumed to have typed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SendInputTextInjector : ITextInjector
{
    // SendInput has no documented cap, but chunking keeps any one call's
    // array small and bounds how much gets silently coalesced by the input
    // queue under load.
    private const int ChunkSize = 64;

    public bool Inject(string text)
    {
        try
        {
            return InjectCore(text);
        }
        catch
        {
            // The port contract is "never throw" — any P/Invoke or marshaling
            // failure becomes a plain false, exactly like a partial SendInput.
            return false;
        }
    }

    private static bool InjectCore(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        var events = new List<INPUT>(text.Length * 2);
        // One down/up pair per UTF-16 code unit: a surrogate pair (an
        // astral-plane character) becomes two code units sent back to back,
        // which Windows reassembles on the receiving end.
        foreach (char codeUnit in text)
        {
            events.Add(MakeKeyEvent(codeUnit, keyUp: false));
            events.Add(MakeKeyEvent(codeUnit, keyUp: true));
        }

        int cbSize = Marshal.SizeOf<INPUT>();
        for (int offset = 0; offset < events.Count; offset += ChunkSize)
        {
            int count = Math.Min(ChunkSize, events.Count - offset);
            var chunk = events.GetRange(offset, count).ToArray();

            uint sent = NativeMethods.SendInput((uint)chunk.Length, chunk, cbSize);
            if (sent < (uint)chunk.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static INPUT MakeKeyEvent(char codeUnit, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = codeUnit,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };
}
