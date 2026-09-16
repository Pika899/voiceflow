using System.Diagnostics;
using System.Runtime.Versioning;

namespace VoiceFlow.App;

/// <summary>
/// One message box per error code the app can raise, mirroring the wording of
/// <c>StatusBarController.presentAlert(for:)</c> on the Mac (adjusted for
/// Windows terminology: "Windows Settings" instead of "System Settings", and
/// the fixed Windows shortcut Ctrl+Alt+Space instead of Control+Option+Space).
/// Plain <see cref="MessageBox"/>, since its two stock buttons are enough for
/// every code here — a custom dialog is reserved for the download progress
/// form, which needs a live progress bar a message box cannot show.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ErrorDialogs
{
    private const string Caption = "VoiceFlow";

    /// <summary>
    /// Shows the dialog for <paramref name="code"/>. <paramref name="retryDownload"/>
    /// is invoked when the user picks the affirmative button on a code that
    /// offers a download/re-download (model-missing, model-load-failed); it is
    /// ignored for every other code. <paramref name="pushToTalkKeyLabel"/> is
    /// the currently configured push-to-talk key's display name ("Ctrl" or
    /// "Ctrl+Alt+Space"); it is used only for "hotkey-conflict", so the dialog
    /// names the key that actually failed instead of assuming which one it was.
    /// </summary>
    public static void Show(string code, IWin32Window? owner, Action? retryDownload, string pushToTalkKeyLabel)
    {
        switch (code)
        {
            case "microphone-permission":
                if (ShowOkCancel(owner,
                        "Microphone access needed",
                        "VoiceFlow needs microphone access to transcribe your speech. Click OK to open Windows Settings > Privacy & security > Microphone, or Cancel."))
                {
                    OpenSettings("ms-settings:privacy-microphone");
                }
                break;

            case "model-missing":
                if (ShowOkCancel(owner,
                        "Model not downloaded",
                        "The speech model hasn't been downloaded yet, or failed a corruption check. Click OK to download it now, or Cancel."))
                {
                    retryDownload?.Invoke();
                }
                break;

            case "model-load-failed":
                // Checksum passed but Whisper.net refused the file: offer the
                // same re-download path as a missing model rather than a bare
                // error (mirrors the Mac's "model-load-failed" case).
                if (ShowOkCancel(owner,
                        "Model couldn't be loaded",
                        "The speech model is present but failed to load. Click OK to download it again, or Cancel."))
                {
                    retryDownload?.Invoke();
                }
                break;

            case "model-download-failed":
                ShowOk(owner,
                    "Download failed",
                    "Something went wrong downloading the speech model, or the downloaded file didn't match what was expected. Check your connection and try again from Settings.");
                break;

            case "hotkey-conflict":
                ShowOk(owner,
                    "Hotkey already in use",
                    $"{pushToTalkKeyLabel} is already in use by another application, or couldn't be registered. Pick a different push-to-talk key in Settings, or free the shortcut in the other app, then try again.");
                break;

            case "transcription-failed" or "transcription-timeout":
                ShowOk(owner,
                    "Transcription failed",
                    "Something went wrong during transcription. Try again.");
                break;

            case "audio-start-failed":
                ShowOk(owner,
                    "Couldn't start recording",
                    "VoiceFlow couldn't open the microphone. Check that one is connected and not in use by another app, then try again.");
                break;

            case "injection-failed":
                ShowOk(owner,
                    "Couldn't type the text",
                    "The transcription succeeded, but VoiceFlow couldn't insert it into the active app. Click into a text field and try again.");
                break;

            default:
                ShowOk(owner, "Unexpected error", code);
                break;
        }
    }

    private static bool ShowOkCancel(IWin32Window? owner, string title, string text)
    {
        var result = owner is null
            ? MessageBox.Show(text, $"{Caption} — {title}", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
            : MessageBox.Show(owner, text, $"{Caption} — {title}", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        return result == DialogResult.OK;
    }

    private static void ShowOk(IWin32Window? owner, string title, string text)
    {
        if (owner is null)
        {
            MessageBox.Show(text, $"{Caption} — {title}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(owner, text, $"{Caption} — {title}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Last-resort dialog for an exception caught at a message-loop boundary
    /// (see TrayApp's try/catch around its event handlers) — never the
    /// expected path, so it carries the raw exception message rather than
    /// one of the curated texts above.
    /// </summary>
    public static void ShowUnexpected(IWin32Window? owner, Exception exception)
    {
        ShowOk(owner, "Unexpected error", $"VoiceFlow hit an unexpected error: {exception.Message}");
    }

    private static void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // A missing settings URI handler must not crash a tray app the
            // user has no other way to recover from; the dialog they just
            // read already told them where to go manually.
        }
    }
}
