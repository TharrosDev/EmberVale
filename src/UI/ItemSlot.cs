using Embervale.Items;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The shared item vocabulary (Phase 37.5C): one slot widget and one detail card, used by the
/// character screen, the storage window and the crafting window so the three read as one system
/// rather than three lists that happen to contain items.
///
/// Slots are <see cref="Button"/>s rather than panels, which is what gets them keyboard and
/// gamepad focus, hover states and activation for free from <see cref="UiTheme"/>'s interactive
/// styling — a grid built from <c>PanelContainer</c>s would need all three reimplemented.
/// </summary>
public static class ItemSlot
{
    public const float DefaultSize = 52f;

    /// <summary>
    /// One inventory cell: rarity frame, category glyph (or the item's <c>Icon</c> if one is ever
    /// authored), and a stack count in the corner.
    ///
    /// <paramref name="selected"/> draws the ember selection rule. It is a separate signal from
    /// focus on purpose: a controller player moves *focus* across the grid to browse, and the
    /// selected item is the one the detail pane is describing. Collapsing the two would mean the
    /// pane changed every time the stick twitched.
    /// </summary>
    public static Button Build(ItemInstance? instance, int quantity = 1, bool selected = false, float size = DefaultSize)
    {
        var slot = new Button
        {
            CustomMinimumSize = new Vector2(size, size),
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            ClipText = true,
        };

        ItemRarity rarity = instance?.Rarity ?? ItemRarity.Common;
        StyleBoxFlat normal = instance is null ? UiTheme.WellStyle() : UiTheme.RarityFrame(rarity);

        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = UiTheme.CardBg;

        StyleBoxFlat focus = (StyleBoxFlat)normal.Duplicate();
        focus.BorderColor = UiTheme.Accent;
        focus.SetBorderWidthAll(2);

        if (selected)
        {
            normal = (StyleBoxFlat)focus.Duplicate();
        }

        slot.AddThemeStyleboxOverride("normal", normal);
        slot.AddThemeStyleboxOverride("hover", hover);
        slot.AddThemeStyleboxOverride("pressed", hover);
        slot.AddThemeStyleboxOverride("focus", focus);

        if (instance is null)
        {
            return slot;
        }

        slot.TooltipText = Tooltip(instance, quantity);

        // Authored item art wins. Data without bespoke art still uses the shared Embervale vector
        // family rather than platform-dependent Unicode symbols.
        if (instance.Template.Icon is { } icon)
        {
            var art = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            slot.AddChild(art);
        }
        else
        {
            TextureRect glyph = UiIcon.Create(IconOf(instance.Type), size * 0.52f, UiTheme.RarityColor(rarity));
            glyph.SetAnchorsPreset(Control.LayoutPreset.Center);
            glyph.OffsetLeft = -size * 0.26f;
            glyph.OffsetTop = -size * 0.26f;
            glyph.OffsetRight = size * 0.26f;
            glyph.OffsetBottom = size * 0.26f;
            slot.AddChild(glyph);
        }

        if (quantity > 1)
        {
            Label count = UiTheme.Caption(quantity.ToString(), UiTheme.Text);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Bottom;
            count.MouseFilter = Control.MouseFilterEnum.Ignore;
            count.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            count.OffsetRight = -3;
            count.OffsetBottom = -2;
            slot.AddChild(count);
        }

        return slot;
    }

    /// <summary>
    /// The detail card for the selected item: name in its rarity colour, a category/weight/value
    /// line, affixes as chips, and the flavour text in the book italic.
    ///
    /// <paramref name="equipped"/>, when given, adds the comparison block — the thing the old text
    /// list could not express at all. Pass the item currently worn in the candidate's slot.
    /// </summary>
    public static Control Detail(ItemInstance instance, ItemInstance? equipped = null, bool compare = false)
    {
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label name = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        col.AddChild(name);

        col.AddChild(UiTheme.Caption(Loc.TF(
            "item.meta",
            Loc.T(TypeKey(instance.Type)),
            instance.Weight.ToString("0.0"),
            instance.Value)));

        if (instance.HasAffixes)
        {
            var chips = new HBoxContainer();
            chips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            foreach (ItemAffix affix in instance.Affixes)
            {
                chips.AddChild(UiTheme.Chip(affix.DisplayValue, UiTheme.Good));
            }

            col.AddChild(chips);
        }

        if (compare)
        {
            Control? delta = Comparison(instance, equipped);
            if (delta is not null)
            {
                col.AddChild(UiTheme.Divider());
                col.AddChild(delta);
            }
        }

        if (!string.IsNullOrWhiteSpace(instance.Template.Description))
        {
            col.AddChild(UiTheme.Flavour(instance.Template.Description));
        }

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        pad.AddChild(col);
        card.AddChild(pad);
        return card;
    }

    /// <summary>
    /// The equipped-vs-candidate stat block, or null when the two are identical (in which case
    /// there is nothing to say and a "no change" line would be clutter).
    ///
    /// Every row is prefixed with +/- as well as coloured, so the comparison survives the 37.5G
    /// colourblind modes — this is the one place in the UI where getting a colour wrong means
    /// equipping the worse item.
    /// </summary>
    private static Control? Comparison(ItemInstance candidate, ItemInstance? equipped)
    {
        var deltas = ItemPresentation.Compare(candidate, equipped);
        if (deltas.Count == 0)
        {
            return null;
        }

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 1);
        col.AddChild(UiTheme.Caption(Loc.T(equipped is null ? "item.vs_empty" : "item.vs_equipped")));

        foreach ((StatType stat, float delta) in deltas)
        {
            bool up = delta > 0f;
            col.AddChild(UiTheme.Caption(
                $"{(up ? "+" : "−")} {StatNames.Label(stat)}  {delta:+0.##;-0.##}",
                up ? UiTheme.Good : UiTheme.Bad));
        }

        return col;
    }

    private static string Tooltip(ItemInstance instance, int quantity)
    {
        string count = quantity > 1 ? $" ×{quantity}" : string.Empty;
        return $"{instance.DisplayName}{count}";
    }

    /// <summary>The <c>Loc</c> key for an item category. Categories are shown in the detail card
    /// and in the backpack's filter row, so they need real localised names rather than the enum's
    /// identifier.</summary>
    public static string TypeKey(ItemType type) => type switch
    {
        ItemType.Consumable => "item.type_consumable",
        ItemType.Weapon => "item.type_weapon",
        ItemType.Armor => "item.type_armor",
        ItemType.Material => "item.type_material",
        ItemType.Quest => "item.type_quest",
        _ => "item.type_misc",
    };

    private static UiIcon.Kind IconOf(ItemType type) => type switch
    {
        ItemType.Consumable => UiIcon.Kind.Consumable,
        ItemType.Weapon => UiIcon.Kind.Weapon,
        ItemType.Armor => UiIcon.Kind.Armor,
        ItemType.Material => UiIcon.Kind.Material,
        ItemType.Quest => UiIcon.Kind.Quest,
        _ => UiIcon.Kind.Misc,
    };
}
