using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>The Controls section of <see cref="SettingsPanel"/>.</summary>
public partial class SettingsPanel
{
    private void BuildControls(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.controls"));
        body.AddChild(SliderRow(Loc.T("settings.mouse_sensitivity"), 0.05, 2.0, 0.05, s.MouseSensitivity,
            v => s.MouseSensitivity = (float)v));
        body.AddChild(ToggleRow(Loc.T("settings.invert_y"), s.InvertY, v => { s.InvertY = v; Persist(); }));
    }
}
