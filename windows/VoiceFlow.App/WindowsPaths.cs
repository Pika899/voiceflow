namespace VoiceFlow.App;

/// <summary>Per-user file locations, matching the Mac's Application Support / models layout.</summary>
public static class WindowsPaths
{
    public static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceFlow", "settings.json");

    public static string ModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceFlow", "models");
}
