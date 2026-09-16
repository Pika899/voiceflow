namespace VoiceFlow.App;

internal static class Program
{
    // Local\ prefix keeps this mutex scoped to the current user session,
    // matching a per-user tray app (a Local\ mutex from one Remote Desktop
    // session is invisible to another, unlike Global\).
    private const string SingleInstanceMutexName = @"Local\VoiceFlow.SingleInstance";

    [STAThread]
    static void Main()
    {
        // Held for the whole process lifetime via `createdNew`/`mutex` both
        // staying alive in this method's scope (Application.Run blocks until
        // the app exits) — a second launch sees createdNew == false and exits
        // immediately without ever showing a tray icon.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new TrayApp());
    }
}
