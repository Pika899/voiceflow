namespace VoiceFlow.Core;

public enum WhisperModelName
{
    Base,
    Small,
    Medium,
}

public enum DictationLanguage
{
    Italian,
    English,
}

/// <summary>
/// The two fixed push-to-talk options (mirrors the Mac's fn/Control+Option+Space
/// pair — no free rebind in v1). <see cref="Ctrl"/> is a bare modifier and is
/// delivered through a low-level keyboard hook rather than RegisterHotKey; see
/// VoiceFlow.App.CtrlKeyMonitor.
/// </summary>
public enum PushToTalkKey
{
    Ctrl,
    CtrlAltSpace,
}

public static class DictationLanguageExtensions
{
    public static string WhisperCode(this DictationLanguage language) => language switch
    {
        DictationLanguage.Italian => "it",
        DictationLanguage.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };
}

public sealed record Settings(
    WhisperModelName Model = WhisperModelName.Base,
    DictationLanguage Language = DictationLanguage.Italian,
    bool PlaySounds = true,
    bool LaunchAtLogin = false,
    PushToTalkKey PushToTalkKey = PushToTalkKey.Ctrl);
