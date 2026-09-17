using System.Runtime.Versioning;

namespace VoiceFlow.App;

/// <summary>
/// Small modal progress dialog for a model download: a label, a determinate
/// progress bar and a Cancel button. Mirrors the Mac's <c>NSAlert</c> with an
/// <c>NSProgressIndicator</c> accessory view in <c>startModelDownload()</c>.
/// The caller owns the actual download (<see cref="ModelLifecycle"/>); this
/// form only displays progress and exposes <see cref="Cts"/> so Cancel can be
/// wired to it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DownloadForm : Form
{
    private readonly ProgressBar progressBar;

    public CancellationTokenSource Cts { get; } = new();

    public DownloadForm(string modelName)
    {
        Text = "VoiceFlow";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 100);

        var label = new Label
        {
            Text = $"Downloading {modelName} model...",
            Location = new Point(12, 12),
            Size = new Size(296, 20),
        };

        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Location = new Point(12, 38),
            Size = new Size(296, 20),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(233, 66),
            Size = new Size(75, 25),
        };
        cancelButton.Click += (_, _) =>
        {
            Cts.Cancel();
            Close();
        };

        Controls.Add(label);
        Controls.Add(progressBar);
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
    }

    /// <summary>Updates the bar from an <c>IProgress&lt;double&gt;</c> callback (0.0-1.0).</summary>
    public void ReportProgress(double fraction)
    {
        if (IsDisposed)
        {
            return;
        }

        int percent = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
        progressBar.Value = percent;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cts.Dispose();
        }

        base.Dispose(disposing);
    }
}
