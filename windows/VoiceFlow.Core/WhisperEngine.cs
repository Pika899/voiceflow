using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace VoiceFlow.Core;

public sealed record TranscriptionResult(string Text, TimeSpan Duration);

public sealed class WhisperEngineException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class WhisperEngine : IDisposable
{
    private readonly WhisperFactory factory;

    public WhisperEngine(string modelPath)
    {
        try
        {
            factory = WhisperFactory.FromPath(modelPath);
        }
        catch (Exception ex)
        {
            throw new WhisperEngineException($"Failed to load whisper model at '{modelPath}'.", ex);
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(float[] samples16k, string languageCode, CancellationToken ct = default)
    {
        // Nothing captured (hotkey tapped without speaking): don't hand
        // whisper an empty buffer, just report an empty transcription.
        if (samples16k.Length == 0)
        {
            return new TranscriptionResult("", TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();

        try
        {
            // Whisper.net defaults to all hardware threads; whisper.cpp's own
            // whisper_full_default_params (what the macOS engine inherits) caps
            // n_threads at min(4, hardware_concurrency), so match that here.
            using var processor = factory.CreateBuilder()
                .WithLanguage(languageCode)
                .WithThreads(Math.Min(4, Environment.ProcessorCount))
                .Build();

            await foreach (var segment in processor.ProcessAsync(samples16k, ct).ConfigureAwait(false))
            {
                text.Append(segment.Text);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WhisperEngineException("Whisper transcription failed.", ex);
        }

        stopwatch.Stop();
        return new TranscriptionResult(Trim(text.ToString()), stopwatch.Elapsed);
    }

    public static string Trim(string text) => text.Trim();

    public void Dispose() => factory.Dispose();
}
