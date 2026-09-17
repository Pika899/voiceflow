using System.IO;
using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class SettingsStoreTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "voiceflow-settings-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void LoadReturnsDefaultsWhenFileIsMissing()
    {
        var dir = CreateTempDirectory();
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));

            var settings = store.Load();

            Assert.Equal(WhisperModelName.Base, settings.Model);
            Assert.Equal(DictationLanguage.Italian, settings.Language);
            Assert.True(settings.PlaySounds);
            Assert.False(settings.LaunchAtLogin);
            Assert.Equal(PushToTalkKey.Ctrl, settings.PushToTalkKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoadRoundTripsEveryField()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            var store = new SettingsStore(filePath);
            var settings = new Settings(
                Model: WhisperModelName.Medium,
                Language: DictationLanguage.English,
                PlaySounds: false,
                LaunchAtLogin: true,
                PushToTalkKey: PushToTalkKey.CtrlAltSpace);

            store.Save(settings);
            var loaded = store.Load();

            Assert.Equal(settings, loaded);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(PushToTalkKey.Ctrl)]
    [InlineData(PushToTalkKey.CtrlAltSpace)]
    public void SaveThenLoadRoundTripsEachPushToTalkKey(PushToTalkKey key)
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            var store = new SettingsStore(filePath);
            var settings = new Settings(PushToTalkKey: key);

            store.Save(settings);
            var loaded = store.Load();

            Assert.Equal(key, loaded.PushToTalkKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDefaultsPushToTalkKeyToCtrlWhenFileHasNoSuchProperty()
    {
        // A settings.json written before this feature existed has no
        // "pushToTalkKey" property at all; loading it must not throw and
        // must fall back to the default (Ctrl), not merely the enum's
        // underlying zero value coincidentally matching it.
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            File.WriteAllText(filePath, """
                {
                  "model": "base",
                  "language": "italian",
                  "playSounds": true,
                  "launchAtLogin": false
                }
                """);
            var store = new SettingsStore(filePath);

            var settings = store.Load();

            Assert.Equal(PushToTalkKey.Ctrl, settings.PushToTalkKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFallsBackToDefaultsWhenPushToTalkKeyIsAnUnknownString()
    {
        // Documents existing SettingsStore behaviour (shared with every other
        // enum field here): JsonStringEnumConverter throws JsonException on
        // an unrecognized member, and Load()'s catch-all turns that into a
        // full defaults fallback for the whole file, not just that field.
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            File.WriteAllText(filePath, """{ "pushToTalkKey": "shift" }""");
            var store = new SettingsStore(filePath);

            var settings = store.Load();

            Assert.Equal(new Settings(), settings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveWritesLowercaseEnumNamesToJson()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            var store = new SettingsStore(filePath);
            var settings = new Settings(
                Model: WhisperModelName.Base,
                Language: DictationLanguage.Italian);

            store.Save(settings);
            var json = File.ReadAllText(filePath);

            Assert.Contains("\"model\": \"base\"", json);
            Assert.Contains("\"language\": \"italian\"", json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsDefaultsWhenFileIsCorrupt()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            File.WriteAllText(filePath, "{ this is not valid json ");
            var store = new SettingsStore(filePath);

            var settings = store.Load();

            Assert.Equal(WhisperModelName.Base, settings.Model);
            Assert.Equal(DictationLanguage.Italian, settings.Language);
            Assert.True(settings.PlaySounds);
            Assert.False(settings.LaunchAtLogin);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsDefaultsWhenFileIsEmpty()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "settings.json");
            File.WriteAllText(filePath, "");
            var store = new SettingsStore(filePath);

            var settings = store.Load();

            Assert.Equal(WhisperModelName.Base, settings.Model);
            Assert.Equal(DictationLanguage.Italian, settings.Language);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveCreatesParentDirectoryWhenMissing()
    {
        var dir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(dir, "nested", "subdir", "settings.json");
            var store = new SettingsStore(filePath);

            store.Save(new Settings());

            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WhisperCodeReturnsWhisperLanguageCodes()
    {
        Assert.Equal("it", DictationLanguage.Italian.WhisperCode());
        Assert.Equal("en", DictationLanguage.English.WhisperCode());
    }
}
