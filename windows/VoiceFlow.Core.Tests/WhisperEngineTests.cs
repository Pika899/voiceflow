using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class WhisperEngineTests
{
    [Fact]
    public void TrimRemovesLeadingAndTrailingWhitespace()
    {
        Assert.Equal("ciao mondo", WhisperEngine.Trim("  ciao mondo  \n"));
    }

    [Fact]
    public void TrimOfEmptyStringIsEmpty()
    {
        Assert.Equal("", WhisperEngine.Trim(""));
    }

    [Fact]
    public void TrimOfWhitespaceOnlyIsEmpty()
    {
        Assert.Equal("", WhisperEngine.Trim("   \n\t "));
    }

    // Gated on VOICEFLOW_TEST_MODEL so the wider suite runs without a
    // downloaded ggml model; on this Mac point it at
    // ~/Library/Application Support/VoiceFlow/models/ggml-base.bin.
    [Fact]
    public async Task TranscribeAsyncRunsThePipelineOnSilence()
    {
        var modelPath = Environment.GetEnvironmentVariable("VOICEFLOW_TEST_MODEL");
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Skipped: VOICEFLOW_TEST_MODEL is unset or does not point to an existing ggml model.
            return;
        }

        using var engine = new WhisperEngine(modelPath);
        var silence = new float[16_000]; // 1 second of silence at 16 kHz

        var result = await engine.TranscribeAsync(silence, "en");

        Assert.NotNull(result.Text);
    }
}
