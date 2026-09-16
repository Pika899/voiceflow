using System.Text;

namespace VoiceFlow.App;

/// <summary>
/// Local, append-only diagnostic log at <see cref="WindowsPaths.LogFile"/>
/// (Ruling R-W7). Records error codes with their full exception chain and
/// per-dictation timings/counts only — it never contains the dictated text,
/// the audio, or the transcription result. Plain file I/O: no Windows API
/// involved, so this class carries no platform attribute.
/// </summary>
public static class DiagnosticLog
{
    // Design parameter, not a measurement: caps the log at roughly a day or
    // two of normal use on a machine that never restarts, rather than
    // implementing rotation for what is meant to answer "what just
    // happened", not serve as a long-term audit trail. When exceeded, the
    // whole file is dropped and logging starts fresh.
    private const long MaxSizeBytes = 1024 * 1024;

    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Lock = new();

    /// <summary>
    /// Appends one <c>yyyy-MM-dd HH:mm:ss.fff  &lt;line&gt;</c> entry. Creates
    /// the log directory if needed and swallows every I/O exception —
    /// logging must never take the app down.
    /// </summary>
    public static void Write(string line)
    {
        try
        {
            lock (Lock)
            {
                var path = WindowsPaths.LogFile;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path) && new FileInfo(path).Length > MaxSizeBytes)
                {
                    File.Delete(path);
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(path, $"{timestamp}  {line}{Environment.NewLine}", NoBomUtf8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>
    /// Logs an error code and, when an exception was in hand, its full
    /// chain and stack trace (indented on the following lines).
    /// </summary>
    public static void Error(string code, Exception? ex)
    {
        if (ex is null)
        {
            Write($"error {code}");
            return;
        }

        var indented = string.Join(
            Environment.NewLine,
            ex.ToString().Replace("\r\n", "\n").Split('\n').Select(l => "    " + l));
        Write($"error {code}{Environment.NewLine}{indented}");
    }
}
