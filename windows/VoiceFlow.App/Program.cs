namespace VoiceFlow.App;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        // Placeholder context; replaced by the tray application shell in Task 8.
        Application.Run(new ApplicationContext());
    }
}
