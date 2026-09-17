using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceFlow.Core;

public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public Settings Load()
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Settings>(json, SerializerOptions) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Missing, empty, corrupt or unreadable settings file: fall back to
            // defaults rather than crashing the caller on startup.
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(filePath, json);
    }
}
