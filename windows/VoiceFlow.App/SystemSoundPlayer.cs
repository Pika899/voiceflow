using System.Media;
using System.Runtime.Versioning;
using VoiceFlow.Core;

namespace VoiceFlow.App;

/// <summary>Start/stop cues from the built-in Windows system sound set — no assets to bundle, mirroring <c>SoundPlayer.swift</c>'s use of stock macOS sounds.</summary>
[SupportedOSPlatform("windows")]
public sealed class SystemSoundPlayer : ISoundPlayer
{
    public void PlayStart() => SystemSounds.Asterisk.Play();

    public void PlayStop() => SystemSounds.Beep.Play();
}
