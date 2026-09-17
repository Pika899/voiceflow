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
