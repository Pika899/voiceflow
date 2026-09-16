using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>
/// Small fixed-size settings window, mirroring <c>SettingsPopoverView.swift</c>:
/// model, language, sound and launch-at-login controls, a static push-to-talk
/// note (v1 has no rebind UI — see the Windows port's task brief) and a Quit
/// button, since a tray app with no Dock icon and no menu bar has no other way
/// to quit. Every field saves through <see cref="SettingsStore"/> immediately;
/// a model change calls back into <see cref="TrayApp"/> so it can reload or
/// re-prompt for a download.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsForm : Form
{
    private static readonly (string Label, WhisperModelName Value)[] ModelOptions =
    [
        ("Base (faster)", WhisperModelName.Base),
        ("Small (more accurate)", WhisperModelName.Small),
        ("Medium (most accurate, slower)", WhisperModelName.Medium),
    ];

    private static readonly (string Label, DictationLanguage Value)[] LanguageOptions =
    [
        ("Italiano", DictationLanguage.Italian),
        ("English", DictationLanguage.English),
    ];

    private readonly SettingsStore settingsStore;
    private readonly Func<Settings> getSettings;
    private readonly Action<Settings> setSettings;
    private readonly Action onModelChanged;
    private readonly Action onQuit;

    private readonly ComboBox modelCombo;
    private readonly ComboBox languageCombo;
    private readonly CheckBox playSoundsCheck;
    private readonly CheckBox launchAtLoginCheck;
    private readonly Label launchAtLoginErrorLabel;

    // Guards every field's changed-event handler while the constructor is
    // populating initial values, and separately guards the launch-at-login
    // checkbox while it reverts itself after a failed StartupRegistration
    // call — in both cases the programmatic assignment must not be mistaken
    // for a user edit and re-trigger the handler (mirrors
    // SettingsViewModel.isRevertingLaunchAtLogin).
    private bool isLoading = true;
    private bool isRevertingLaunchAtLogin;

    public SettingsForm(SettingsStore settingsStore, Func<Settings> getSettings, Action<Settings> setSettings, Action onModelChanged, Action onQuit)
    {
        this.settingsStore = settingsStore;
        this.getSettings = getSettings;
        this.setSettings = setSettings;
        this.onModelChanged = onModelChanged;
        this.onQuit = onQuit;

        Text = "VoiceFlow Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 300);

        var modelLabel = new Label { Text = "Model:", Location = new Point(12, 15), Size = new Size(100, 20) };
        modelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(110, 12),
            Size = new Size(198, 23),
        };
        modelCombo.Items.AddRange(ModelOptions.Select(o => o.Label).ToArray());

        var languageLabel = new Label { Text = "Language:", Location = new Point(12, 48), Size = new Size(100, 20) };
        languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(110, 45),
            Size = new Size(198, 23),
        };
        languageCombo.Items.AddRange(LanguageOptions.Select(o => o.Label).ToArray());

        playSoundsCheck = new CheckBox { Text = "Play sounds", Location = new Point(12, 82), Size = new Size(280, 24) };

        launchAtLoginCheck = new CheckBox { Text = "Launch at login", Location = new Point(12, 110), Size = new Size(280, 24) };

        launchAtLoginErrorLabel = new Label
        {
            Location = new Point(12, 136),
            Size = new Size(296, 32),
            ForeColor = Color.Red,
            Visible = false,
        };

        var noteLabel = new Label
        {
            Text = "Push-to-talk: hold Ctrl+Alt+Space",
            Location = new Point(12, 176),
            Size = new Size(296, 20),
            ForeColor = SystemColors.GrayText,
        };

        var quitButton = new Button
        {
            Text = "Quit VoiceFlow",
            Location = new Point(197, 235),
            Size = new Size(111, 28),
        };
        quitButton.Click += (_, _) => onQuit();

        Controls.Add(modelLabel);
        Controls.Add(modelCombo);
        Controls.Add(languageLabel);
        Controls.Add(languageCombo);
        Controls.Add(playSoundsCheck);
        Controls.Add(launchAtLoginCheck);
        Controls.Add(launchAtLoginErrorLabel);
        Controls.Add(noteLabel);
        Controls.Add(quitButton);

        var current = getSettings();
        modelCombo.SelectedIndex = Array.FindIndex(ModelOptions, o => o.Value == current.Model);
        languageCombo.SelectedIndex = Array.FindIndex(LanguageOptions, o => o.Value == current.Language);
        playSoundsCheck.Checked = current.PlaySounds;
        launchAtLoginCheck.Checked = current.LaunchAtLogin;
        isLoading = false;

        modelCombo.SelectedIndexChanged += OnModelChanged;
        languageCombo.SelectedIndexChanged += OnLanguageChanged;
        playSoundsCheck.CheckedChanged += OnPlaySoundsChanged;
        launchAtLoginCheck.CheckedChanged += OnLaunchAtLoginChanged;
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (isLoading)
        {
            return;
        }

        Save(s => s with { Model = ModelOptions[modelCombo.SelectedIndex].Value });
        onModelChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (isLoading)
        {
            return;
        }

        Save(s => s with { Language = LanguageOptions[languageCombo.SelectedIndex].Value });
    }

    private void OnPlaySoundsChanged(object? sender, EventArgs e)
    {
        if (isLoading)
        {
            return;
        }

        Save(s => s with { PlaySounds = playSoundsCheck.Checked });
    }

    private void OnLaunchAtLoginChanged(object? sender, EventArgs e)
    {
        if (isLoading || isRevertingLaunchAtLogin)
        {
            return;
        }

        bool desired = launchAtLoginCheck.Checked;
        try
        {
            StartupRegistration.SetEnabled(desired);
            launchAtLoginErrorLabel.Visible = false;
            launchAtLoginErrorLabel.Text = string.Empty;
            Save(s => s with { LaunchAtLogin = desired });
        }
        catch (Exception ex)
        {
            // Never let the checkbox claim a state the OS refused: put it
            // back and say why, instead of persisting a lie (mirrors
            // SettingsViewModel.launchAtLogin's catch block).
            isRevertingLaunchAtLogin = true;
            launchAtLoginCheck.Checked = !desired;
            isRevertingLaunchAtLogin = false;
            launchAtLoginErrorLabel.Text = $"Couldn't update startup setting: {ex.Message}";
            launchAtLoginErrorLabel.Visible = true;
        }
    }

    private void Save(Func<Settings, Settings> update)
    {
        var updated = update(getSettings());
        settingsStore.Save(updated);
        setSettings(updated);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            // Reused for the lifetime of the app (TrayApp keeps a single
            // instance and shows/activates it) rather than recreated on every
            // click of the tray icon — clicking the window's own X just hides it.
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
