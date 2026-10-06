using Godot;

namespace Embervale.UI;

/// <summary>The front of the shell's own measurements and builders: the boot splash, the title,
/// first-run setup, the loading screen and the credits.</summary>
public static partial class UiTheme
{
    /// <summary>Width of the title's menu column.</summary>
    public const float TitleSheetWidth = 400f;

    /// <summary>The seal above the title's wordmark, and alone on the boot splash.</summary>
    public const float TitleSealSize = 72f;
    public const float SplashSealSize = 168f;

    /// <summary>Below this logical height the title drops its seal and subtitle so the seven
    /// entries still fit (a handheld at 853x533).</summary>
    public const float TitleShortHeight = UiChromeRules.ShortHeight;

    /// <summary>Width of the loading screen's text block (realm name, progress line, card).</summary>
    public const float LoadingBlockWidth = 460f;

    /// <summary>Thickness of the loading screen's progress line.</summary>
    public const float LoadingLineHeight = 2f;

    /// <summary>Width of the credits column.</summary>
    public const float CreditsColumnWidth = 620f;

    /// <summary>The widest first-run setup grows, and the widths of its two inner columns.</summary>
    public const float FirstRunSheetWidth = 860f;
    public const float FirstRunControlColumn = 220f;
    public const float FirstRunSampleWidth = 300f;

    /// <summary>How far the title's painting is drawn past the view on every side, which is the
    /// room its drift moves in.</summary>
    public const float BackdropOverscan = 18f;

    /// <summary>The painting every shell screen falls back to.</summary>
    public const string GenericPainting = "menu_ashen_causeway";

    private const string PaintingFolder = "res://assets/ui/backgrounds/";

    /// <summary>
    /// A full-screen painting from <c>assets/ui/backgrounds/</c> by file name (no extension), or
    /// the causeway when it cannot be loaded, or null when that cannot either (an unimported
    /// checkout): the caller then shows its scrim alone.
    /// </summary>
    public static Texture2D? Painting(string name)
    {
        string path = $"{PaintingFolder}{name}.png";
        if (ResourceLoader.Exists(path) && GD.Load<Texture2D>(path) is { } painting)
        {
            return painting;
        }

        string fallback = $"{PaintingFolder}{GenericPainting}.png";
        return name != GenericPainting && ResourceLoader.Exists(fallback) ? GD.Load<Texture2D>(fallback) : null;
    }

    /// <summary>A painting laid over the whole view, cropped to cover it.</summary>
    public static TextureRect Cover(Texture2D? painting)
    {
        var rect = new TextureRect
        {
            Texture = painting,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>
    /// A soft full-screen shade over a painting: <see cref="ScrimBg"/> running from clear at
    /// <paramref name="from"/> to <paramref name="opacity"/> at <paramref name="to"/> (both in
    /// 0..1 of the view). What sits under a frameless column so its text reads without a box;
    /// under high contrast it is nearly solid where the text is.
    /// </summary>
    public static TextureRect Shade(Vector2 from, Vector2 to, float opacity, bool radial = false)
    {
        Color clear = ScrimBg with { A = 0f };
        Color dark = ScrimBg with { A = HighContrast ? 0.97f : opacity };
        var rect = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                // A radial fill runs outward from its first point, so there the dark end leads.
                Gradient = new Gradient
                {
                    Offsets = new[] { 0f, 1f },
                    Colors = radial ? new[] { dark, clear } : new[] { clear, dark },
                },
                Width = 128,
                Height = 128,
                Fill = radial ? GradientTexture2D.FillEnum.Radial : GradientTexture2D.FillEnum.Linear,
                FillFrom = radial ? to : from,
                FillTo = radial ? from : to,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>Moves a <see cref="Sheet"/>'s column to the right of the view, 8% in from that
    /// edge: the shell's paintings keep their calm space on the right.</summary>
    public static void SheetToRight(VBoxContainer column, float width)
    {
        if (column.GetParent()?.GetParent() is not MarginContainer frame)
        {
            return;
        }

        frame.AnchorLeft = 0.92f;
        frame.AnchorRight = 0.92f;
        frame.OffsetLeft = -width;
        frame.OffsetRight = 0f;
    }

    /// <summary>
    /// One entry of the title's menu: a word in the carved face with no plate under it. Hover
    /// warms the word and draws a rule at its left; focus draws the brighter focus rule there and
    /// lifts a faint face behind it, so the state is a shape as well as a colour.
    /// </summary>
    public static Button TitleEntry(string text, UiCue cue = UiCue.Click)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0f, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplyType(button, FontRole.Display, HeaderFontSize);
        button.AddThemeStyleboxOverride("normal", TitleEntryStyle(0, false));
        button.AddThemeStyleboxOverride("disabled", TitleEntryStyle(0, false));
        button.AddThemeStyleboxOverride("hover", TitleEntryStyle(2, false));
        button.AddThemeStyleboxOverride("pressed", TitleEntryStyle(2, true));
        button.AddThemeStyleboxOverride("focus", TitleEntryStyle(HighContrast ? 5 : 3, true));
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_pressed_color", Accent);
        button.AddThemeColorOverride("font_hover_pressed_color", Accent);
        button.AddThemeColorOverride("font_focus_color", Accent);
        button.AddThemeColorOverride("font_disabled_color", Disabled);
        button.Pressed += () => UiAudio.Play(cue);
        return button;
    }

    private static StyleBoxFlat TitleEntryStyle(int rule, bool face)
    {
        var box = new StyleBoxFlat
        {
            BgColor = face ? ButtonFaceFocus with { A = HighContrast ? 1f : 0.55f } : ButtonFaceFocus with { A = 0f },
            BorderColor = face ? FocusRing : RuleLit,
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = rule;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(0);

        // The same in every state, so a word never shifts when its rule appears.
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceMd;
        return box;
    }
}
