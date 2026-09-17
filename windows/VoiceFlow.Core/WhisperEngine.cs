using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace VoiceFlow.Core;

public sealed record TranscriptionResult(string Text, TimeSpan Duration, int AudioContext = 0);

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
        var audioContext = AudioContextFor(samples16k.Length);

        try
        {
            // Whisper.net defaults to all hardware threads; whisper.cpp's own
            // whisper_full_default_params (what the macOS engine inherits) caps
            // n_threads at min(4, hardware_concurrency), so match that here.
            using var processor = factory.CreateBuilder()
                .WithLanguage(languageCode)
                .WithThreads(Math.Min(4, Environment.ProcessorCount))
                .WithAudioContextSize(audioContext)
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
        return new TranscriptionResult(Trim(text.ToString()), stopwatch.Elapsed, audioContext);
    }

    public static string Trim(string text) => text.Trim();

    // Ruling R-W8: whisper's encoder always runs its fixed 30 s window
    // (n_audio_ctx = 1500, 50 frames/s) regardless of how much audio was
    // actually captured, which is why transcription time on a slow/no-AVX2
    // CPU stays roughly constant no matter how short the dictation is.
    // whisper.cpp exposes n_audio_ctx to shrink that window to the audio at
    // hand; its own real-time `stream` example runs with -ac 768. This sizes
    // the context to the dictation instead of always paying for 30 s:
    // ceil(seconds * 50) frames for the audio, one second of margin, floored
    // at whisper.cpp's stream example value (below it hallucinations grow)
    // and capped at the model's maximum window.
    private const int FramesPerSecond = 50; // whisper's n_audio_ctx=1500 covers a 30 s window
    private const int MarginFrames = 50; // one second of headroom
    private const int MinAudioContext = 768; // floor from whisper.cpp's stream example
    private const int MaxAudioContext = 1500; // the model's maximum encoder context

    /// <summary>
    /// Sizes the whisper encoder's audio context to a dictation of
    /// <paramref name="sampleCount"/> 16 kHz mono samples. Design parameters
    /// (see the ruling above), not a measurement — the effect is measured by
    /// reading <c>audioCtx</c> back out of the diagnostic log.
    /// </summary>
    public static int AudioContextFor(int sampleCount)
    {
        var seconds = sampleCount / 16_000.0;
        var frames = (int)Math.Ceiling(seconds * FramesPerSecond) + MarginFrames;
        return Math.Clamp(frames, MinAudioContext, MaxAudioContext);
    }

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
