using Godot;

namespace Embervale.UI;

/// <summary>
/// The session shell's share of the theme (2026-10 UI upgrade, lane "Shell session"): the
/// measurements of the save-slot browser, the pause and death sheets, the character creator and
/// the narration cards, and the text-style action a sheet's entries are. Everything here is built
/// from the tokens in <c>UiTheme.cs</c>; no new colours.
/// </summary>
public static partial class UiTheme
{
    /// <summary>The widest the slot browser and the creator grow; past this an ultrawide only adds margin.</summary>
    public const float SessionSheetMaxWidth = 1080f;

    /// <summary>Width of the pause sheet's column.</summary>
    public const float PauseSheetWidth = 380f;

    /// <summary>Width of the death sheet's column.</summary>
    public const float DeathSheetWidth = 420f;

    /// <summary>Width of a confirm prompt's text.</summary>
    public const float SessionPromptWidth = 380f;

    /// <summary>A save's thumbnail in its row: 16:9, like the screenshot it is cut from.</summary>
    public const float SlotThumbWidth = 112f;

    /// <summary>See <see cref="SlotThumbWidth"/>.</summary>
    public const float SlotThumbHeight = 63f;

    /// <summary>Width of one button in a slot row, so Load and Delete are equal targets.</summary>
    public const float SlotActionWidth = 96f;

    /// <summary>Smallest height the slot list scrolls in: three controls. The sheet gives it the rest.</summary>
    public const float SlotListMinHeight = ControlHeight * 3f;

    /// <summary>Edge of a slot row's kind and corruption glyphs.</summary>
    public const float SlotGlyphSize = 14f;

    /// <summary>Below this logical width the creator's rail and preview give up width to the options.</summary>
    public const float CreatorNarrowWidth = 1000f;

    /// <summary>Width of the creator's step rail, wide and narrow.</summary>
    public const float CreatorRailWidth = 176f;

    /// <summary>See <see cref="CreatorRailWidth"/>.</summary>
    public const float CreatorRailNarrowWidth = 132f;

    /// <summary>Width of the creator's preview column, wide and narrow.</summary>
    public const float CreatorPreviewWidth = 320f;

    /// <summary>See <see cref="CreatorPreviewWidth"/>.</summary>
    public const float CreatorPreviewNarrowWidth = 200f;

    /// <summary>The share of a wide sheet the preview column takes, and the most it grows to.</summary>
    public const float CreatorPreviewShare = 0.34f;

    /// <summary>See <see cref="CreatorPreviewShare"/>.</summary>
    public const float CreatorPreviewWideWidth = 368f;

    /// <summary>How dark the death sheet's scrim is: the HUD under it must not read through.</summary>
    public const float DeathScrim = 0.9f;

    /// <summary>The wash over an ending's painting, and how much darker the soft band behind the
    /// card's text and the one under the hint row are at their fullest.</summary>
    public const float NarrationWash = 0.6f;

    /// <summary>See <see cref="NarrationWash"/>.</summary>
    public const float NarrationBand = 0.78f;

    /// <summary>Smallest height of the preview and of the options list beside it.</summary>
    public const float CreatorBodyMinHeight = ControlHeight * 3f;

    /// <summary>The widest a narration card's text runs.</summary>
    public const float NarrationMeasure = 680f;

    /// <summary>Gap between the entries of a sheet or a rail: none. Each is a full control tall and
    /// carries its own margins, and the plate focus lifts behind one should meet its neighbours.</summary>
    public const int SessionEntryGap = 0;

    /// <summary>How long the death sheet takes to darken the frame.</summary>
    public const float DurationDeath = 0.8f;

    /// <summary>
    /// A text-style action: a sheet's entry (pause, death) or a step on the creator's rail. No
    /// face until it is used: hover lights the text, focus lifts a plate behind it with the focus
    /// ring down its leading edge. <paramref name="lit"/> is the chosen entry of a rail, which
    /// keeps a plate and a lit edge while focus is elsewhere.
    /// </summary>
    public static Button SessionAction(string text, UiCue cue = UiCue.Click, bool lit = false)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0f, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        ApplyType(button, FontRole.Display, BodyFontSize);
        button.AddThemeColorOverride("font_color", lit ? Accent : Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_focus_color", Accent);
        button.AddThemeColorOverride("font_pressed_color", AccentHot);
        button.AddThemeColorOverride("font_hover_pressed_color", AccentHot);
        button.AddThemeColorOverride("font_disabled_color", Disabled);

        StyleBoxFlat rest = SessionActionStyle(focused: false, lit);
        button.AddThemeStyleboxOverride("normal", rest);
        button.AddThemeStyleboxOverride("disabled", rest);
        button.AddThemeStyleboxOverride("hover", SessionActionStyle(focused: false, lit: true));
        button.AddThemeStyleboxOverride("pressed", SessionActionStyle(focused: false, lit: true));
        button.AddThemeStyleboxOverride("focus", SessionActionStyle(focused: true, lit));
        button.Pressed += () => UiAudio.Play(cue);
        return button;
    }

    /// <summary>The ground of a <see cref="SessionAction"/>. The margins are the same in every
    /// state, so focus moving down a sheet never shifts a word.</summary>
    public static StyleBoxFlat SessionActionStyle(bool focused, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = focused ? ButtonFaceFocus : lit ? CardBg : CardBg with { A = 0f },
            BorderColor = focused ? FocusRing : HighContrast ? RuleLit with { A = 1f } : RuleLit,
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = focused ? (HighContrast ? 4 : 3) : lit ? 2 : 0;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(SpaceXs);
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceMd;
        return box;
    }

    /// <summary>A hairline across a sheet's column: the cold seam between two groups on one surface.</summary>
    public static ColorRect SessionRule() => new()
    {
        Color = HighContrast ? Rule with { A = 1f } : Rule,
        CustomMinimumSize = new Vector2(0f, 1f),
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };
}
