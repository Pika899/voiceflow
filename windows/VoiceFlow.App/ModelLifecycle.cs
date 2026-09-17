using System.Diagnostics;
using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Owns the <see cref="WhisperEngine"/>'s lifecycle: loading the currently
/// configured model if it is present and valid, and running a visible
/// download when it isn't. Mirrors <c>StatusBarController</c>'s
/// <c>loadModelIfPresent()</c>/<c>startModelDownload()</c> on the Mac,
/// including which path disposes the previous engine (only a successful
/// reload does — a missing or unloadable model leaves whatever engine was
/// already running untouched) and the stale/cancelled-download ruling.
///
/// Error presentation is left to the caller via <paramref name="onError"/>:
/// only the composition root (<see cref="TrayApp"/>) knows whether a dialog
/// is already on screen and needs to defer this one. <paramref name="onError"/>
/// also carries the exception behind the code (null for "model-missing",
/// which has none) — these errors never reach <see cref="DictationController"/>,
/// so its own <c>LastErrorException</c> would be the wrong, unrelated source.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ModelLifecycle : IDisposable
{
    private readonly ModelManager modelManager;
    private readonly Func<Settings> settings;
    private readonly Action<string, Exception?> onError;
    private readonly Action onEngineLoaded;

    // Identifies the download whose completion is still welcome. Cancel (via
    // the form's own Cts) short-circuits before this is even consulted; this
    // guards a *different* download having superseded this one by the time it
    // finishes, so a stale completion can't report an error for a download
    // the user has moved on from (mirrors StatusBarController.activeDownloadID).
    private Guid activeDownloadId = Guid.Empty;

    public WhisperEngine? Engine { get; private set; }

    public ModelLifecycle(ModelManager modelManager, Func<Settings> settings, Action<string, Exception?> onError, Action onEngineLoaded)
    {
        this.modelManager = modelManager;
        this.settings = settings;
        this.onError = onError;
        this.onEngineLoaded = onEngineLoaded;
    }

    /// <summary>
    /// Loads the engine for the currently configured model. Safe to call
    /// again after a model change or a completed download.
    /// </summary>
    public void LoadModelIfPresent()
    {
        var model = ModelCatalogue.For(settings().Model);
        if (!modelManager.IsModelPresentAndValid(model))
        {
            onError("model-missing", null);
            return;
        }

        try
        {
            var localPath = modelManager.LocalPath(model);
            var stopwatch = Stopwatch.StartNew();
            var newEngine = new WhisperEngine(localPath);
            stopwatch.Stop();

            Engine?.Dispose();
            Engine = newEngine;
            DiagnosticLog.Write($"model-loaded {localPath} duration={stopwatch.ElapsedMilliseconds}ms");
        }
        catch (WhisperEngineException ex)
        {
            // The exception travels through onError to TrayApp, which logs
            // it there (the single place that logs every error code) — this
            // exception never reaches DictationController.LastErrorException,
            // since it is caught before the controller is even involved.
            onError("model-load-failed", ex);
        }
    }

    /// <summary>
    /// Downloads the currently configured model with a visible progress
    /// dialog. Blocks the caller (pumping messages, like the Mac's
    /// <c>NSAlert.runModal()</c>) until the dialog closes, whether by
    /// completion, failure or Cancel.
    /// </summary>
    public void StartDownload(IWin32Window? owner)
    {
        var model = ModelCatalogue.For(settings().Model);
        var downloadId = Guid.NewGuid();
        activeDownloadId = downloadId;

        var form = new DownloadForm(model.Name);
        // Progress<T> captures SynchronizationContext.Current at construction
        // time; building it here, on the UI thread, is what makes its
        // callback marshal back onto the UI thread regardless of which thread
        // DownloadAsync's internal ConfigureAwait(false) continuations land on.
        var progress = new Progress<double>(form.ReportProgress);

        _ = RunDownloadAsync(model, progress, form.Cts, downloadId, form);
        form.ShowDialog(owner);
    }

    private async Task RunDownloadAsync(ModelInfo model, IProgress<double> progress, CancellationTokenSource cts, Guid downloadId, DownloadForm form)
    {
        try
        {
            await modelManager.DownloadAsync(model, progress, cts.Token);
            CloseAndDispose(form);
            if (activeDownloadId != downloadId)
            {
                return; // superseded by a newer download; stay silent
            }

            LoadModelIfPresent();
            if (Engine is not null)
            {
                onEngineLoaded();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancel: no error, no reload. Whatever error led here
            // (model-missing/model-load-failed) is left alone rather than
            // re-prompting immediately.
            CloseAndDispose(form);
        }
        catch (Exception ex) when (ex is ChecksumMismatchException or ModelDownloadException)
        {
            CloseAndDispose(form);
            if (activeDownloadId == downloadId)
            {
                onError("model-download-failed", ex);
            }
        }
    }

    private static void CloseAndDispose(DownloadForm form)
    {
        if (form.IsDisposed)
        {
            return;
        }

        if (form.Visible)
        {
            form.Close();
        }

        form.Dispose();
    }

    public void Dispose() => Engine?.Dispose();
}
