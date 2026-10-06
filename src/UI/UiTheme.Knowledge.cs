using Godot;

namespace Embervale.UI;

/// <summary>
/// The builders the knowledge screens share (journal, map, bestiary, dialogue), so the hub's
/// screens read as one family: the same plate, the same title row, the same gutters.
/// </summary>
public static partial class UiTheme
{
    // --- The map plot -----------------------------------------------------------------------
    // A chart drawn on smoked vellum: darker than a card and lighter than the panel it is cut
    // into, so the plot belongs to the page instead of sitting on it as a pale slab. The land runs
    // from MapLandLow in the valleys to MapLandHigh on the ridges; roads, pins and lettering are
    // the light things on it. Opaque, so a road or a label reads the same over every part of it.

    /// <summary>Ground nobody has charted: the colour under the whole plot.</summary>
    public static readonly Color MapDeep = new(0.030f, 0.030f, 0.027f);

    /// <summary>The tint the vellum texture is drawn through.</summary>
    public static readonly Color MapVellum = new(0.30f, 0.275f, 0.235f, 0.62f);

    /// <summary>Charted land at its lowest and at its highest. The relief shades between them.</summary>
    public static readonly Color MapLandLow = new(0.150f, 0.138f, 0.116f);
    public static readonly Color MapLandHigh = new(0.262f, 0.238f, 0.196f);

    /// <summary>A cell's footprint where a region has no baked relief to draw.</summary>
    public static readonly Color MapLand = new(0.19f, 0.172f, 0.144f, 0.86f);

    /// <summary>Standing water: cold, and a step darker than the shore beside it.</summary>
    public static readonly Color MapWater = new(0.095f, 0.150f, 0.185f);

    /// <summary>A road's worn core, drawn over an <see cref="Engrave"/> shoulder.</summary>
    public static readonly Color MapRoad = new(0.62f, 0.575f, 0.49f);

    /// <summary>A track or lane: the same line, quieter.</summary>
    public static readonly Color MapTrack = new(0.47f, 0.44f, 0.38f);

    /// <summary>The face a territory's name is lettered in: carved capitals, or the interface face
    /// when the readable-font setting is on.</summary>
    public static Font? MapLetteringFont => FontFor(FontRole.Display) ?? UiFont;

    /// <summary>
    /// Restyles a panel's shell as a cut plate: the panel ground with one lit edge along the top
    /// and no box round it. The grain <see cref="Panel"/> applied stays.
    /// </summary>
    public static void ApplyHubPlate(PanelContainer shell)
    {
        var box = new StyleBoxFlat
        {
            BgColor = PanelBg,
            BorderColor = HighContrast ? RuleLit with { A = 1f } : RuleLit,
        };
        box.SetBorderWidthAll(0);
        box.BorderWidthTop = HighContrast ? 3 : 1;
        box.SetCornerRadiusAll(RadiusSm);
        box.ShadowColor = Engrave;
        box.ShadowSize = HighContrast ? SpaceXs : SpaceSm;
        box.ShadowOffset = new Vector2(0f, Space2xs);
        shell.AddThemeStyleboxOverride("panel", box);
    }

    /// <summary>
    /// The page every hub screen in this family starts from: the plate, the standard padding, a
    /// title row, an optional row of sub-tabs and a hairline. Returns the column the screen adds
    /// its body to; <paramref name="aside"/> is the right-hand end of the title row, for the one
    /// line of context a screen has (where the player is, how much is catalogued).
    /// </summary>
    public static VBoxContainer HubPage(PanelContainer shell, string title, out HBoxContainer aside, Control? tabs = null)
    {
        ApplyHubPlate(shell);

        MarginContainer margin = Padding(PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", SpaceSm);
        margin.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", SpaceMd);
        Label name = Title(title);
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(name);

        aside = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        aside.AddThemeConstantOverride("separation", SpaceSm);
        head.AddChild(aside);
        column.AddChild(head);

        if (tabs != null)
        {
            column.AddChild(TabRail(tabs));
        }

        column.AddChild(RuleRect(HighContrast ? Rule with { A = 1f } : Rule, 1f));
        return column;
    }

    /// <summary>
    /// A row of sub-tabs that never widens its page: the tabs scroll sideways inside it (no bar;
    /// the focused tab is kept in view) when a handheld viewport cannot hold them all.
    /// </summary>
    public static ScrollContainer TabRail(Control tabs)
    {
        var rail = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(0f, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rail.AddChild(tabs);
        return rail;
    }

    /// <summary>A <see cref="Chip(string, Color)"/> with an icon before its text: a creature's
    /// resistance, a place's kind. The icon and the word say the same thing.</summary>
    public static PanelContainer IconChip(UiIcon.Kind icon, string text, Color color)
    {
        PanelContainer chip = ChipShell(color);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Space2xs);
        TextureRect glyph = UiIcon.Create(icon, CaptionFontSize + 2f, color);
        glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(glyph);
        row.AddChild(Caption(text, color));
        chip.AddChild(row);
        return chip;
    }

    /// <summary>
    /// Holds a paragraph to the reading measure (<see cref="JournalLayoutRules.ProseChars"/>
    /// characters): a column <paramref name="available"/> px wide gives up whatever is beyond it
    /// on the right, so prose never runs the width of an ultrawide page.
    /// </summary>
    public static MarginContainer Measure(Control prose, float available)
    {
        var wrap = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrap.AddThemeConstantOverride(
            "margin_right", JournalLayoutRules.ProseInset(available, FontSize(BodyFontSize)));
        prose.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        wrap.AddChild(prose);
        return wrap;
    }
}
