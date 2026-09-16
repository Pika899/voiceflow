using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Draws the tray icon per <see cref="DictationState"/> with GDI+ instead of
/// bundling image files: a filled circle whose color mirrors the Mac's SF
/// Symbol swap (mic / mic.fill / waveform / exclamationmark.triangle) — grey
/// idle, red listening, blue transcribing, orange error.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TrayIcons
{
    // Icon.FromHandle wraps a raw GDI icon handle that Windows never reclaims
    // on its own; normally the caller must call DestroyIcon once done with it.
    // Here every (state, size) combination is drawn at most once and kept in
    // this cache for the whole process lifetime (there is no "done" until the
    // process exits), so there is no point at which DestroyIcon would ever be
    // correct to call on a cached entry.
    private static readonly Dictionary<(DictationState State, int Size), Icon> Cache = new();

    public static Icon For(DictationState state)
    {
        int size = SystemInformation.SmallIconSize.Width >= 24 ? 32 : 16;
        var key = (state, size);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = Draw(state, size);
        Cache[key] = icon;
        return icon;
    }

    private static Icon Draw(DictationState state, int size)
    {
        Color color = state switch
        {
            DictationState.Idle => Color.Gray,
            DictationState.Listening => Color.Red,
            DictationState.Transcribing => Color.DodgerBlue,
            DictationState.Error => Color.Orange,
            _ => Color.Gray,
        };

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float inset = size * 0.15f;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, inset, inset, size - (2 * inset), size - (2 * inset));
        }

        // GetHicon() hands back a handle Icon.FromHandle does not take
        // ownership of; Clone() copies it into an Icon with its own
        // independently-owned handle, so the temporary one can be destroyed
        // right away instead of leaking on every cache miss.
        IntPtr temporaryHandle = bitmap.GetHicon();
        try
        {
            using var wrapper = Icon.FromHandle(temporaryHandle);
            return (Icon)wrapper.Clone();
        }
        finally
        {
            Native.NativeMethods.DestroyIcon(temporaryHandle);
        }
    }
}
