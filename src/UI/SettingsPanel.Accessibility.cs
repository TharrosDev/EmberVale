using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>The Accessibility section of <see cref="SettingsPanel"/>.</summary>
public partial class SettingsPanel
{
    private void BuildAccessibility(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.accessibility"));
        body.AddChild(ToggleRow(Loc.T("settings.reduced_motion"), s.ReducedMotion,
            v => { s.ReducedMotion = v; Persist(); }, Loc.T("settings.reduced_motion.note")));
        body.AddChild(ToggleRow(Loc.T("settings.subtitles"), s.SubtitlesEnabled, v => { s.SubtitlesEnabled = v; Persist(); }));
        body.AddChild(SliderRow(Loc.T("settings.ui_scale"), 0.75, 1.5, 0.05, s.UiScale, v => s.UiScale = (float)v));

        // 37.5G. Text scale is separate from UI scale on purpose: UI scale is the window's content
        // scale factor and magnifies panels, margins and glyphs together, while this touches only
        // glyphs — for a player who wants readable text without surrendering half the screen to
        // chrome. It is floored at the 12 px legibility minimum inside UiTheme.FontSize.
        body.AddChild(SliderRow(Loc.T("settings.text_scale"), 0.85, 1.5, 0.05, s.TextScale,
            v => { s.TextScale = (float)v; Persist(); }, Loc.T("settings.text_scale.note")));

        body.AddChild(ToggleRow(Loc.T("settings.high_contrast"), s.HighContrast,
            v => { s.HighContrast = v; Persist(); }, Loc.T("settings.high_contrast.note")));

        // Colour-vision adaptation daltonizes the UI's semantic ramps — rarity, magic school,
        // faction standing, good/bad — so pairs that would collapse together stay apart. World art
        // is deliberately untouched; see ColorVision.
        body.AddChild(DropdownRow(
            Loc.T("settings.color_vision"),
            new[]
            {
                Loc.T("settings.color_vision.none"),
                Loc.T("settings.color_vision.deuteranopia"),
                Loc.T("settings.color_vision.protanopia"),
                Loc.T("settings.color_vision.tritanopia"),
            },
            (int)s.ColorVision,
            index => { s.ColorVision = (ColorVisionMode)index; Persist(); },
            Loc.T("settings.color_vision.note")));
    }
}
