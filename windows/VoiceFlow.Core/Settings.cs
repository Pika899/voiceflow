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
    bool LaunchAtLogin = false);
