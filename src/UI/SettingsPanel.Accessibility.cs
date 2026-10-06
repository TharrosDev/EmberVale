using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>The Accessibility tab of <see cref="SettingsPanel"/>: how large and how plain the
/// interface is, how much it moves, how it is confirmed, and how subtitles are drawn.</summary>
public partial class SettingsPanel
{
    private void BuildAccessibility(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.vision"), first: true);

        // Both wait for the drag to end: each resizes the screen the slider is on.
        body.AddChild(SliderRow(
            Info(Loc.T("settings.ui_scale"), Loc.T("settings.ui_scale.desc"), nameof(S.UiScale)),
            0.75, 1.5, 0.05, s.UiScale, v => s.UiScale = v, Percent, applyOnRelease: true));

        // 37.5G. Text scale is separate from UI scale on purpose: UI scale is the window's content
        // scale factor and magnifies panels, margins and glyphs together, while this touches only
        // glyphs — for a player who wants readable text without surrendering half the screen to
        // chrome. It is floored at the 12 px legibility minimum inside UiTheme.FontSize.
        RowInfo textScale = Info(Loc.T("settings.text_scale"), Loc.T("settings.text_scale.note"), nameof(S.TextScale));
        textScale.Preview = () => TextSample(textScale);
        body.AddChild(SliderRow(textScale, 0.85, 1.5, 0.05, s.TextScale, v => s.TextScale = v, Percent,
            applyOnRelease: true));

        // The four below change how every surface on this sheet is drawn, so it is rebuilt.
        RowInfo readable = Info(Loc.T("settings.readable_font"), Loc.T("settings.readable_font.desc"), nameof(S.ReadableFont));
        readable.Preview = () => TextSample(readable);
        body.AddChild(ToggleRow(readable, s.ReadableFont, v => { s.ReadableFont = v; Persist(); MarkDirty(); }));

        RowInfo contrast = Info(Loc.T("settings.high_contrast"), Loc.T("settings.high_contrast.note"), nameof(S.HighContrast));
        contrast.Preview = VisionSwatches;
        body.AddChild(ToggleRow(contrast, s.HighContrast, v => { s.HighContrast = v; Persist(); MarkDirty(); }));

        // Colour-vision adaptation daltonizes the UI's semantic ramps — rarity, magic school,
        // faction standing, good/bad — so pairs that would collapse together stay apart. World art
        // is deliberately untouched; see ColorVision.
        RowInfo vision = Info(Loc.T("settings.color_vision"), Loc.T("settings.color_vision.note"), nameof(S.ColorVision));
        vision.Preview = VisionSwatches;
        body.AddChild(DropdownRow(vision,
            new[]
            {
                Loc.T("settings.color_vision.none"),
                Loc.T("settings.color_vision.deuteranopia"),
                Loc.T("settings.color_vision.protanopia"),
                Loc.T("settings.color_vision.tritanopia"),
            },
            (int)s.ColorVision,
            index => { s.ColorVision = (ColorVisionMode)index; Persist(); MarkDirty(); }));

        Section(body, Loc.T("settings.section.motion_input"));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.reduced_motion"), Loc.T("settings.reduced_motion.desc"), nameof(S.ReducedMotion)),
            s.ReducedMotion, v => { s.ReducedMotion = v; Persist(); }));

        // Rebuilt: the reset buttons on this sheet trade their hold rings for a confirm.
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.holds_to_presses"), Loc.T("settings.holds_to_presses.desc"), nameof(S.HoldsToPresses)),
            s.HoldsToPresses, v => { s.HoldsToPresses = v; Persist(); MarkDirty(); }));

        Section(body, Loc.T("settings.section.subtitles"));
        RowInfo subtitles = Info(Loc.T("settings.subtitles"), Loc.T("settings.subtitles.desc"), nameof(S.SubtitlesEnabled));
        subtitles.Preview = () => SubtitleSample(subtitles);
        body.AddChild(ToggleRow(subtitles, s.SubtitlesEnabled, v => { s.SubtitlesEnabled = v; Persist(); }));

        RowInfo size = Info(Loc.T("settings.subtitle_size"), Loc.T("settings.subtitle_size.desc"), nameof(S.SubtitleSize));
        size.Preview = () => SubtitleSample(size);
        body.AddChild(DropdownRow(size,
            new[]
            {
                Loc.T("settings.subtitle_size.small"), Loc.T("settings.subtitle_size.medium"),
                Loc.T("settings.subtitle_size.large"),
            },
            SettingsMath.ClampSubtitleSize(s.SubtitleSize), index => { s.SubtitleSize = index; Persist(); }));

        RowInfo plate = Info(Loc.T("settings.subtitle_background"), Loc.T("settings.subtitle_background.desc"),
            nameof(S.SubtitleBackground));
        plate.Preview = () => SubtitleSample(plate);
        body.AddChild(SliderRow(plate, 0.0, 1.0, 0.05, Mathf.Clamp(s.SubtitleBackground, 0f, 1f),
            v => s.SubtitleBackground = v, Percent));

        RowInfo speaker = Info(Loc.T("settings.subtitle_speaker"), Loc.T("settings.subtitle_speaker.desc"),
            nameof(S.SubtitleSpeakerNames));
        speaker.Preview = () => SubtitleSample(speaker);
        body.AddChild(ToggleRow(speaker, s.SubtitleSpeakerNames, v => { s.SubtitleSpeakerNames = v; Persist(); }));
    }
}
