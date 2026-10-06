using Godot;

namespace Embervale.UI;

/// <summary>
/// The builders the trade screens share (vendor, crafting, storage, appraisal, contract board), so
/// the five read as one family with the hub screens though they carry no hub strip: the same cut
/// plate, title row and gutters as <see cref="HubPage"/>, with the screen's one ornament (the
/// ember wipe) as the rule under the title. Built from the tokens in <c>UiTheme.cs</c>; no new colours.
/// </summary>
public static partial class UiTheme
{
    /// <summary>
    /// The page a trade screen starts from: the plate, the standard padding, a title row (an icon,
    /// the name, and <paramref name="aside"/> at its right-hand end for purses or a skill readout),
    /// an optional row of sub-tabs and the ember wipe. Returns the column the screen adds its body
    /// to. The title is set later with <see cref="SetTradeTitle"/>, because a counter is named when
    /// it is opened; replay <paramref name="wipe"/> with <c>UiOrnament.PlayEmberWipe</c>.
    /// </summary>
    public static VBoxContainer TradePage(
        PanelContainer shell, UiIcon.Kind icon, out Label title, out HBoxContainer aside, out Control wipe, Control? tabs = null)
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

        TextureRect mark = UiIcon.Create(icon, TitleFontSize + 2f, Accent);
        mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(mark);

        // The name gives way first: it trims, so two purses beside a long shop name never push
        // the page past a handheld's right edge.
        title = Title(string.Empty);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.MouseFilter = Control.MouseFilterEnum.Pass;
        head.AddChild(title);

        aside = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        aside.AddThemeConstantOverride("separation", SpaceLg);
        head.AddChild(aside);
        column.AddChild(head);

        if (tabs != null)
        {
            column.AddChild(TabRail(tabs));
        }

        wipe = UiOrnament.EmberWipe(HighContrast ? 3f : 2f);
        column.AddChild(wipe);
        return column;
    }

    /// <summary>Names a trade page. Carved capitals for a name of three words or fewer, the
    /// interface face for a longer one (UI_STYLE: Cinzel has no lower case).</summary>
    public static void SetTradeTitle(Label title, string text)
    {
        if (title.Text == text)
        {
            return;
        }

        title.Text = text;
        title.TooltipText = text;
        ApplyType(title, ItemPresentation.UsesDisplayFace(text) ? FontRole.Display : FontRole.Interface, TitleFontSize);
    }

    /// <summary>A purse in a page's title row: the coin, whose it is, and how much is in it.
    /// <paramref name="amount"/> is handed back to be rewritten on every rebuild.</summary>
    public static HBoxContainer PurseReadout(out Label owner, out Label amount)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", SpaceXs);

        TextureRect coin = UiIcon.Create(UiIcon.Kind.Currency, BodyFontSize + 3f, Accent);
        coin.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(coin);

        var copy = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        copy.AddThemeConstantOverride("separation", 0);
        owner = Caption(string.Empty);
        copy.AddChild(owner);
        amount = Body(string.Empty, Accent);
        copy.AddChild(amount);
        row.AddChild(copy);
        return row;
    }

    /// <summary>
    /// One titled, scrolling column of a trade page. The heading is a short carved label with a
    /// quiet note after it (slots used, a restock cadence), so the note never ends up in capitals.
    /// A <paramref name="width"/> above zero pins the column; zero lets it share what is left.
    /// </summary>
    public static VBoxContainer TradeColumn(float width, out Label header, out Label note, out VBoxContainer list)
    {
        var side = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", SpaceXs);
        if (width > 0f)
        {
            side.CustomMinimumSize = new Vector2(width, 0f);
        }
        else
        {
            side.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", SpaceSm);
        header = Header(string.Empty);
        head.AddChild(header);
        note = Caption(string.Empty);
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        head.AddChild(note);
        side.AddChild(head);

        (ScrollContainer scroll, VBoxContainer rows) = ScrollList();
        side.AddChild(scroll);
        list = rows;
        return side;
    }

    /// <summary>The cold hairline between two columns of one page.</summary>
    public static ColorRect ColumnRule() => new()
    {
        Color = HighContrast ? Rule with { A = 1f } : Rule,
        CustomMinimumSize = new Vector2(1f, 0f),
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.Fill,
    };

    /// <summary>The frame of a list row on a trade page: a card with the compact margins, so a
    /// handheld's short list still shows five rows.</summary>
    public static StyleBoxFlat TradeRowStyle(Color? edge) => Compact(CardStyle(edge));

    /// <summary>A card's one hero fact: a big number over a quiet word saying what it counts.</summary>
    public static VBoxContainer HeroFact(string value, string unit, Color color)
    {
        var fact = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        fact.AddThemeConstantOverride("separation", 0);

        Label number = Display(value, color);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        fact.AddChild(number);

        Label word = Caption(unit);
        word.HorizontalAlignment = HorizontalAlignment.Right;
        fact.AddChild(word);
        return fact;
    }
}
