using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Captures the default WASAPI input device in shared mode and converts
/// whatever format the device hands back (commonly 48 kHz float32 stereo) to
/// 16 kHz mono float, matching <c>AudioCapture.swift</c>'s contract: a single
/// converter built once per capture, samples buffered under a lock because
/// <see cref="WasapiCapture.DataAvailable"/> fires on NAudio's own capture
/// thread while <see cref="Stop"/> runs on the caller's thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;
    private const uint AccessDeniedHResult = 0x80070005;

    // StopRecording() only requests a stop; NAudio's capture thread finishes
    // asynchronously and signals RecordingStopped once it actually has. This
    // is a design bound on how long Stop() waits for that signal before
    // giving up and draining anyway — not a measured latency.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly object bufferLock = new();
    private readonly List<float> buffer = new();
    private readonly ManualResetEventSlim recordingStoppedEvent = new(initialState: false);

    private WasapiCapture? capture;
    private BufferedWaveProvider? bufferedProvider;
    private ISampleProvider? resampled;

    public void Start()
    {
        WasapiCapture? newCapture = null;
        try
        {
            newCapture = new WasapiCapture();
            var deviceFormat = newCapture.WaveFormat;

            var provider = new BufferedWaveProvider(deviceFormat)
            {
                // Must return only what's actually been captured. The default
                // (true) pads short reads with silence instead of returning
                // less, which would inject silence into real speech every
                // time Drain() outruns the device.
                ReadFully = false,
            };

            ISampleProvider mono = ToMono(provider.ToSampleProvider(), deviceFormat.Channels);
            var resampler = new WdlResamplingSampleProvider(mono, TargetSampleRate);

            lock (bufferLock)
            {
                buffer.Clear();
                bufferedProvider = provider;
                resampled = resampler;
                capture = newCapture;
            }

            recordingStoppedEvent.Reset();
            newCapture.DataAvailable += OnDataAvailable;
            newCapture.RecordingStopped += OnRecordingStopped;

            newCapture.StartRecording();
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            CleanUpFailedStart(newCapture);
            throw new AudioCaptureException("microphone-permission", "Microphone access was denied.", ex);
        }
        catch (Exception ex)
        {
            CleanUpFailedStart(newCapture);
            throw new AudioCaptureException("audio-start-failed", "Could not start audio capture.", ex);
        }
    }

    public float[] Stop()
    {
        WasapiCapture? currentCapture;
        lock (bufferLock)
        {
            currentCapture = capture;
        }
        if (currentCapture is null)
        {
            return Array.Empty<float>();
        }

        currentCapture.StopRecording();
        // Wait for NAudio's capture thread to actually finish — and, per
        // NAudio's event ordering, for its last DataAvailable to have already
        // landed — before unsubscribing and draining. Bounded so a stuck
        // driver can't hang Stop() forever; the samples captured before the
        // timeout are still returned either way.
        recordingStoppedEvent.Wait(StopTimeout);

        currentCapture.DataAvailable -= OnDataAvailable;
        currentCapture.RecordingStopped -= OnRecordingStopped;

        // Whatever is still sitting in the resampler's internal pipeline gets
        // flushed out here, same as the Mac converter draining on stop().
        Drain();

        lock (bufferLock)
        {
            // Locked so a DataAvailable callback that is still in flight (the
            // wait above is a best effort, not a guarantee) can never observe
            // these as non-null and then touch a capture object we're about
            // to dispose.
            capture = null;
            bufferedProvider = null;
            resampled = null;
        }

        currentCapture.Dispose();

        lock (bufferLock)
        {
            return buffer.ToArray();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (bufferLock)
        {
            bufferedProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
        Drain();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Whatever the device reported (including a failure) doesn't
        // invalidate the samples already captured — Stop() still returns
        // them; only the wait itself is what this event unblocks.
        recordingStoppedEvent.Set();
    }

    // Holds bufferLock for the whole read-and-append: the sample chain
    // (bufferedProvider/resampled) is not thread-safe on its own, and both
    // the capture thread (via OnDataAvailable) and the caller's thread (via
    // Stop's final drain) can reach this method around teardown.
    private void Drain()
    {
        var temp = new float[1024];
        lock (bufferLock)
        {
            if (resampled is null)
            {
                return;
            }

            int read;
            while ((read = resampled.Read(temp, 0, temp.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    buffer.Add(temp[i]);
                }
            }
        }
    }

    private void CleanUpFailedStart(WasapiCapture? failedCapture)
    {
        if (failedCapture is not null)
        {
            failedCapture.DataAvailable -= OnDataAvailable;
            failedCapture.RecordingStopped -= OnRecordingStopped;
            failedCapture.Dispose();
        }

        lock (bufferLock)
        {
            capture = null;
            bufferedProvider = null;
            resampled = null;
        }
    }

    // WASAPI shared-mode capture is overwhelmingly stereo or mono in
    // practice; NAudio has no built-in N-channel-to-mono averager, so
    // anything beyond those two is treated as an unsupported device rather
    // than guessed at.
    private static ISampleProvider ToMono(ISampleProvider source, int channels) => channels switch
    {
        1 => source,
        2 => new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f },
        _ => throw new NotSupportedException($"Unsupported capture channel count: {channels}"),
    };

    private static bool IsAccessDenied(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        COMException com => unchecked((uint)com.HResult) == AccessDeniedHResult,
        _ => false,
    };
}
