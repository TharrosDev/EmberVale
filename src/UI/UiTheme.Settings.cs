using Godot;

namespace Embervale.UI;

/// <summary>The settings screen's own measurements and surfaces (<see cref="SettingsPanel"/>).</summary>
public static partial class UiTheme
{
    /// <summary>The widest the settings sheet grows; past this an ultrawide only adds margin.</summary>
    public const float SettingsSheetMaxWidth = 1180f;

    /// <summary>Width of the description pane beside the option list.</summary>
    public const float SettingsPaneWidth = 320f;

    /// <summary>Width of the right-hand control column. Wide enough for the longest dropdown value
    /// and the slider-plus-readout pair, so nothing has to squeeze its label.</summary>
    public const float SettingsControlColumn = 250f;

    /// <summary>Width of one binding cell: a keycap as long as "Wheel Down", or a pad glyph.</summary>
    public const float SettingsBindingCell = 124f;

    /// <summary>Width of a slider's value readout.</summary>
    public const float SettingsReadout = 48f;

    /// <summary>Smallest height the option list scrolls in: three rows. The sheet gives it the rest.</summary>
    public const float SettingsListMinHeight = ControlHeight * 3f;

    /// <summary>Width of a confirm or conflict prompt's text.</summary>
    public const float SettingsPromptWidth = 380f;

    /// <summary>
    /// The ground of one settings row: nothing but a hairline under it, until the row holds focus,
    /// when it lifts to a card with the lit edge. The margins are the same in both states, so
    /// focus moving down the list never shifts a label.
    /// </summary>
    public static StyleBoxFlat SettingsRowStyle(bool focused)
    {
        var box = new StyleBoxFlat
        {
            BgColor = focused ? CardBg : CardBg with { A = 0f },
            BorderColor = focused ? (HighContrast ? FocusRing : RuleLit) : Rule,
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthBottom = focused ? 0 : 1;
        box.BorderWidthLeft = focused ? (HighContrast ? 4 : 2) : 0;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(0);
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceXs;
        return box;
    }
}
