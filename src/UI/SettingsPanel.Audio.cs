using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>The Audio tab of <see cref="SettingsPanel"/>: one volume per mixer bus.</summary>
public partial class SettingsPanel
{
    private void BuildAudio(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.volume"), first: true);
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.master_volume"), Loc.T("settings.master_volume.desc"), nameof(S.MasterVolume)),
            s.MasterVolume, v => s.MasterVolume = v));
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.music_volume"), Loc.T("settings.music_volume.desc"), nameof(S.MusicVolume)),
            s.MusicVolume, v => s.MusicVolume = v));
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.sfx_volume"), Loc.T("settings.sfx_volume.desc"), nameof(S.SfxVolume)),
            s.SfxVolume, v => s.SfxVolume = v));
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.ambience_volume"), Loc.T("settings.ambience_volume.desc"), nameof(S.AmbienceVolume)),
            s.AmbienceVolume, v => s.AmbienceVolume = v));
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.ui_volume"), Loc.T("settings.ui_volume.desc"), nameof(S.UiVolume)),
            s.UiVolume, v => s.UiVolume = v));
        body.AddChild(VolumeRow(
            Info(Loc.T("settings.voice_volume"), Loc.T("settings.voice_volume.desc"), nameof(S.VoiceVolume)),
            s.VoiceVolume, v => s.VoiceVolume = v));
    }

    /// <summary>A 0..1 volume slider with a live % readout; applies live while dragging, persists on
    /// release.</summary>
    private Control VolumeRow(RowInfo info, float value, System.Action<float> assign) =>
        SliderRow(info, 0d, 1d, 0.05d, value, assign, Percent);
}
