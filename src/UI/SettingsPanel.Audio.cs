using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>The Audio section of <see cref="SettingsPanel"/>: one volume per mixer bus.</summary>
public partial class SettingsPanel
{
    private void BuildAudio(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.audio"));
        body.AddChild(VolumeRow(Loc.T("settings.master_volume"), s.MasterVolume, v => s.MasterVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.music_volume"), s.MusicVolume, v => s.MusicVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.sfx_volume"), s.SfxVolume, v => s.SfxVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.ambience_volume"), s.AmbienceVolume, v => s.AmbienceVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.ui_volume"), s.UiVolume, v => s.UiVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.voice_volume"), s.VoiceVolume, v => s.VoiceVolume = v));
    }
}
