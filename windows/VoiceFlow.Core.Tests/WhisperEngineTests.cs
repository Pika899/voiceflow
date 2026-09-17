using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class WhisperEngineTests
{
    [Fact]
    public void TrimRemovesLeadingAndTrailingWhitespace()
    {
        Assert.Equal("ciao mondo", WhisperEngine.Trim("  ciao mondo  \n"));
    }

    // Ruling R-W8: ctx = clamp(ceil(seconds * 50) + 50, 768, 1500).
    [Theory]
    [InlineData(0, 768)] // no audio captured
    [InlineData(16_000, 768)] // 1 s: 50 + 50 = 100 -> floor
    [InlineData(14 * 16_000, 768)] // 14 s: 700 + 50 = 750 -> floor
    [InlineData(15 * 16_000, 800)] // 15 s: 750 + 50 = 800
    [InlineData(29 * 16_000, 1500)] // 29 s: 1450 + 50 = 1500 exactly
    [InlineData(40 * 16_000, 1500)] // 40 s: 2000 + 50 = 2050 -> ceiling
    public void AudioContextForMatchesTheDesignedFormula(int sampleCount, int expected)
    {
        Assert.Equal(expected, WhisperEngine.AudioContextFor(sampleCount));
    }

    [Fact]
    public void AudioContextForIsMonotonicNonDecreasingAcrossASweep()
    {
        var previous = WhisperEngine.AudioContextFor(0);
        for (var seconds = 0; seconds <= 45; seconds++)
        {
            var current = WhisperEngine.AudioContextFor(seconds * 16_000);
            Assert.True(current >= previous, $"AudioContextFor regressed at {seconds}s: {current} < {previous}");
            previous = current;
        }
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

    // Same gating as above. Regression test for the controller ruling: a
    // WhisperEngine.Dispose() that lands while TranscribeAsync is still
    // running its native inference on a thread-pool thread must defer
    // freeing the whisper.cpp context rather than racing it (which would be
    // a process-terminating access violation, not a catchable exception).
    [Fact]
    public async Task DisposeDuringTranscriptionIsDeferredUntilItCompletes()
    {
        var modelPath = Environment.GetEnvironmentVariable("VOICEFLOW_TEST_MODEL");
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Skipped: VOICEFLOW_TEST_MODEL is unset or does not point to an existing ggml model.
            return;
        }

        var engine = new WhisperEngine(modelPath);
        var silence = new float[16_000 * 3]; // ~3 seconds of silence at 16 kHz

        var transcribeTask = engine.TranscribeAsync(silence, "en");
        // Deliberately not awaited above: Dispose() must land while the
        // native inference this kicked off is still running.
        engine.Dispose();

        var result = await transcribeTask;
        Assert.NotNull(result.Text);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.TranscribeAsync(silence, "en"));
    }
}
