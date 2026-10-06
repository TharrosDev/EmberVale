using Godot;

namespace Embervale.UI;

/// <summary>
/// The builders the knowledge screens share (journal, map, bestiary, dialogue), so the hub's
/// screens read as one family: the same plate, the same title row, the same gutters.
/// </summary>
public static partial class UiTheme
{
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
