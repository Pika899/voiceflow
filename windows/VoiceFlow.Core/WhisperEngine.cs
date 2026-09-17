using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace VoiceFlow.Core;

public sealed record TranscriptionResult(string Text, TimeSpan Duration);

public sealed class WhisperEngineException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class WhisperEngine : IDisposable, ITranscriber
{
    private readonly WhisperFactory factory;
    private readonly object lifecycleLock = new();
    private int inFlight;
    private bool disposeRequested;
    private bool factoryDisposed;

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
        lock (lifecycleLock)
        {
            if (disposeRequested)
            {
                throw new ObjectDisposedException(nameof(WhisperEngine));
            }

            inFlight++;
        }

        try
        {
            return await TranscribeCoreAsync(samples16k, languageCode, ct).ConfigureAwait(false);
        }
        finally
        {
            // Unlike ARC on the Mac, a .NET Dispose() does not wait out
            // in-flight callers holding a reference: whoever's the last one
            // out (this finally, or Dispose() itself if no call is running)
            // is the one that actually frees the native whisper.cpp context,
            // so a Dispose() that lands mid-inference never races the native
            // call running on the thread pool below.
            lock (lifecycleLock)
            {
                inFlight--;
                if (disposeRequested && inFlight == 0)
                {
                    DisposeFactory();
                }
            }
        }
    }

    private async Task<TranscriptionResult> TranscribeCoreAsync(float[] samples16k, string languageCode, CancellationToken ct)
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

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            disposeRequested = true;
            if (inFlight == 0)
            {
                DisposeFactory();
            }
        }
    }

    // Always called with lifecycleLock held.
    private void DisposeFactory()
    {
        if (factoryDisposed)
        {
            return;
        }

        factoryDisposed = true;
        factory.Dispose();
    }
}
