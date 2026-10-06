using System.Collections.Generic;
using System.Linq;
using Embervale.Core;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
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

    /// <summary>Slot beside a card row's text (storage, shop, salvage, appraisal): sits inside the 44 px
    /// button height of those rows, so a bigger icon costs no row height.</summary>
    public const float RowSize = 40f;

    /// <summary>Slot beside two stacked text lines with no taller control (the equipment column).</summary>
    public const float CompactSize = 34f;

    /// <summary>
    /// What the detail card needs to know about the player to say more than what the item is:
    /// the gear worn (comparison, and how many pieces of a set are on), and the level (whether a
    /// requirement is met). A screen with no player in hand passes none of it and gets the plain card.
    /// </summary>
    public sealed record DetailContext(EquipmentComponent? Equipment = null, int PlayerLevel = 0, bool Compare = false)
    {
        /// <summary>What the buttons do to this item on the screen showing it, as glyph and verb pairs
        /// for the card's footer (Equip, Sell, Store). The card adds its own Compare entry when there
        /// is something worn to set it beside. Null or empty shows none.</summary>
        public IReadOnlyList<LegendEntry>? Actions { get; init; }
    }

    /// <summary>
    /// The input that flips a detail card between its one-item view and the side-by-side comparison
    /// with what is worn, wherever a card is on screen. Shift on a keyboard and a click of the left
    /// stick on a pad: the one existing action that is bound on both, is free in every menu and does
    /// not collide with accept, cancel, the hub's Q/E or the sub-tab Z/C. A dedicated menu action
    /// would replace it here and nowhere else.
    /// </summary>
    public const string CompareAction = GameInput.Sprint;

    /// <summary>Whether detail cards show the side-by-side comparison. A preference of this run of the
    /// game, like the pack's sort order: it outlives the panel that set it and is not saved.</summary>
    public static bool CompareOpen { get; set; }

    /// <summary>What a slot says about its item beyond the item's own facts. The screen knows these;
    /// the item does not.</summary>
    [System.Flags]
    public enum Marks
    {
        None = 0,

        /// <summary>Came into the pack since it was last opened: a diamond pip in the top-left corner.</summary>
        New = 1,

        /// <summary>Worn right now: a lit left edge.</summary>
        Equipped = 2,
    }

    /// <summary>
    /// One inventory cell: rarity frame, the item's picture (or its category glyph where the atlas has
    /// none), and a stack count on a dark badge in the corner.
    ///
    /// <paramref name="selected"/> draws the ember selection rule. It is a separate signal from
    /// focus on purpose: focus is where the cursor is and wears the brighter focus ring, selection is
    /// the item the detail pane is describing, and the two part company the moment the player moves
    /// to the pane's buttons.
    ///
    /// A locked item carries a padlock in its top-left corner and a junk item a coin and a strike
    /// across the cell; an upgraded one shows its level top-right; rarity is counted out as ticks
    /// along the bottom edge. They are icons, shapes and a number, not tints, so the marks read under
    /// every colour-vision mode.
    /// </summary>
    public static Button Build(ItemInstance? instance, int quantity = 1, bool selected = false, float size = DefaultSize) =>
        Build(instance, quantity, selected, size, Marks.None);

    /// <summary>As the four-argument <c>Build</c>, with the marks only the screen
    /// knows about: new since the pack was last opened, and worn.</summary>
    public static Button Build(ItemInstance? instance, int quantity, bool selected, float size, Marks marks)
    {
        var slot = new Button
        {
            CustomMinimumSize = new Vector2(size, size),

            // Not Flat: a flat button never draws its `normal` stylebox, so the rarity frame and the
            // empty well were skipped entirely and every slot read as a loose glyph with no edge.
            FocusMode = Control.FocusModeEnum.All,
            ClipText = true,
        };

        ApplyFrame(slot, instance, selected);
        if (instance is null)
        {
            return slot;
        }

        ItemRarity rarity = instance.Rarity;
        slot.TooltipText = Tooltip(instance, quantity, marks);

        // Authored item art wins, then the item's archetype on the painted atlas (ItemIcons). Data
        // with neither still uses the shared Embervale vector family rather than platform-dependent
        // Unicode symbols. The picture stops short of the frame at every size, so the rarity rule
        // stays a rule around it and is never painted over.
        float inset = IconInset(size);
        if (ItemIcons.For(instance.Template) is { } icon)
        {
            var art = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            art.OffsetLeft = inset;
            art.OffsetTop = inset;
            art.OffsetRight = -inset;
            art.OffsetBottom = -inset;
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

        bool badged = instance.Locked || instance.Junk;
        float badge = Mathf.Max(12f, size * 0.3f);

        // Over the picture and under the badges: ticks, pip, worn edge and junk strike in one node.
        slot.AddChild(new ItemSlotOverlay(
            rarity,
            (marks & Marks.New) != 0,
            (marks & Marks.Equipped) != 0,
            instance.Junk,
            badged ? badge + 3f : inset - 1f));

        if (badged)
        {
            var ground = new ColorRect { Color = UiTheme.Keyline, MouseFilter = Control.MouseFilterEnum.Ignore };
            ground.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            ground.OffsetLeft = 1f;
            ground.OffsetTop = 1f;
            ground.OffsetRight = 3f + badge;
            ground.OffsetBottom = 3f + badge;
            slot.AddChild(ground);

            TextureRect mark = instance.Locked
                ? UiIcon.Create(UiIcon.Kind.Lock, badge, UiTheme.Accent)
                : UiIcon.Create(UiIcon.Kind.Currency, badge, UiTheme.Text);
            mark.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            mark.OffsetLeft = 2f;
            mark.OffsetTop = 2f;
            mark.OffsetRight = 2f + badge;
            mark.OffsetBottom = 2f + badge;
            slot.AddChild(mark);
        }

        if (quantity > 1)
        {
            slot.AddChild(Badge(quantity.ToString(), UiTheme.Text, Control.LayoutPreset.BottomRight));
        }

        if (instance.UpgradeLevel > 0)
        {
            slot.AddChild(Badge($"+{instance.UpgradeLevel}", UiTheme.Good, Control.LayoutPreset.TopRight));
        }

        return slot;
    }

    /// <summary>
    /// An empty equipment slot as something to press: the well with the ghost of what goes in it (a
    /// weapon, a piece of armour, a trinket), named in its tooltip. A bare well reads as decoration;
    /// the silhouette says a thing belongs here.
    /// </summary>
    public static Button BuildEmpty(EquipmentSlot slot, float size = CompactSize, bool selected = false)
    {
        Button cell = Build(null, 1, selected, size, Marks.None);
        UiIcon.Kind kind = EquipmentSlots.FamilyOf(slot) switch
        {
            GearFamily.Weapon => UiIcon.Kind.Weapon,
            GearFamily.Armor => UiIcon.Kind.Armor,
            _ => UiIcon.Kind.Misc,
        };

        TextureRect ghost = UiIcon.Create(kind, size * 0.5f, UiTheme.Disabled);
        ghost.SetAnchorsPreset(Control.LayoutPreset.Center);
        ghost.OffsetLeft = -size * 0.25f;
        ghost.OffsetTop = -size * 0.25f;
        ghost.OffsetRight = size * 0.25f;
        ghost.OffsetBottom = size * 0.25f;
        cell.AddChild(ghost);
        cell.TooltipText = Loc.TF("item.slot_empty_tip", EquipmentSlots.Label(slot));
        return cell;
    }

    /// <summary>Redraws a built slot's frame as selected or not, in place. A screen whose selection
    /// follows focus moves the ember rule this way instead of rebuilding its grid on every step.</summary>
    public static void SetSelected(Button slot, ItemInstance? instance, bool selected) => ApplyFrame(slot, instance, selected);

    /// <summary>Takes the new pip off a built slot, once the player has looked at the item.</summary>
    public static void ClearNew(Button slot)
    {
        foreach (Node child in slot.GetChildren())
        {
            if (child is ItemSlotOverlay overlay)
            {
                overlay.ClearNew();
            }
        }
    }

    /// <summary>How far a slot's picture stops short of its edge, so the frame shows at every size.</summary>
    public static float IconInset(float size) => Mathf.Max(3f, Mathf.Round(size * 0.08f));

    private static void ApplyFrame(Button slot, ItemInstance? instance, bool selected)
    {
        ItemRarity rarity = instance?.Rarity ?? ItemRarity.Common;
        StyleBoxFlat normal = instance is null ? UiTheme.WellStyle() : UiTheme.RarityFrame(rarity);

        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = UiTheme.CardBg;

        StyleBoxFlat focus = (StyleBoxFlat)normal.Duplicate();
        focus.BorderColor = UiTheme.FocusRing;
        focus.SetBorderWidthAll(2);

        if (selected)
        {
            normal = (StyleBoxFlat)normal.Duplicate();
            normal.BorderColor = UiTheme.Accent;
            normal.SetBorderWidthAll(2);
        }

        slot.AddThemeStyleboxOverride("normal", normal);
        slot.AddThemeStyleboxOverride("hover", hover);
        slot.AddThemeStyleboxOverride("pressed", hover);
        slot.AddThemeStyleboxOverride("focus", focus);
    }

    /// <summary>A caption on a dark badge, pinned in one corner of a slot and grown inward from it.</summary>
    private static PanelContainer Badge(string text, Color color, Control.LayoutPreset corner)
    {
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", UiTheme.BadgeStyle());

        Label label = UiTheme.Caption(text, color);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        plate.AddChild(label);

        bool top = corner == Control.LayoutPreset.TopRight;
        plate.SetAnchorsPreset(corner);
        plate.GrowHorizontal = Control.GrowDirection.Begin;
        plate.GrowVertical = top ? Control.GrowDirection.End : Control.GrowDirection.Begin;
        plate.OffsetLeft = -2f;
        plate.OffsetRight = -2f;
        plate.OffsetTop = top ? 2f : -2f;
        plate.OffsetBottom = top ? 2f : -2f;
        return plate;
    }

    /// <summary>
    /// The detail card for the selected item: a plate with one lit edge in the rarity colour, the name
    /// and type on its band, one hero number, the stat rows, affixes as chips, the flavour text in the
    /// book italic and a footer with what it weighs and costs.
    ///
    /// <paramref name="equipped"/>, when given, adds the comparison: the deltas on the hero number and
    /// on every stat row, and the side-by-side view on <see cref="CompareAction"/>. Pass the item
    /// currently worn in the candidate's slot.
    /// </summary>
    public static Control Detail(ItemInstance instance, ItemInstance? equipped = null, bool compare = false)
    {
        var rivals = new List<(EquipmentSlot Slot, ItemInstance? Item)>();
        if (compare && instance.Equippable is { } gear)
        {
            rivals.Add((gear.Slot, equipped));
        }

        return BuildDetail(instance, rivals, null, 0, null);
    }

    /// <summary>
    /// The detail card with the player in view. The comparison is worked out here rather than by the
    /// caller: a ring is compared against every ring slot (<see cref="ItemPresentation.RivalSlots"/>),
    /// and something already worn is compared against nothing, since it is the baseline.
    /// </summary>
    public static Control Detail(ItemInstance instance, DetailContext context)
    {
        var rivals = new List<(EquipmentSlot Slot, ItemInstance? Item)>();
        if (context.Compare && context.Equipment is { } worn && instance.Equippable is { } gear &&
            !worn.IsInstanceEquipped(instance))
        {
            foreach (EquipmentSlot slot in ItemPresentation.RivalSlots(gear.Slot))
            {
                rivals.Add((slot, worn.GetEquipped(slot)));
            }
        }

        return BuildDetail(instance, rivals, context.Equipment, context.PlayerLevel, context.Actions);
    }

    private static Control BuildDetail(
        ItemInstance instance,
        IReadOnlyList<(EquipmentSlot Slot, ItemInstance? Item)> rivals,
        EquipmentComponent? equipment,
        int playerLevel,
        IReadOnlyList<LegendEntry>? actions)
    {
        Color rarityColor = UiTheme.RarityColor(instance.Rarity);
        var card = new ItemDetailCard(rarityColor, UiTheme.RarityEdgeWidth(instance.Rarity));
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);

        // The band: the name in its rarity colour, and the rarity again as a word in the type line.
        PanelContainer band = UiTheme.PlateBand(rarityColor);
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.LineGap);
        Label name = UiTheme.Body(instance.DisplayName, rarityColor);
        UiTheme.ApplyType(
            name,
            ItemPresentation.UsesDisplayFace(instance.DisplayName) ? UiTheme.FontRole.Display : UiTheme.FontRole.Interface,
            UiTheme.HeaderFontSize);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.AddChild(name);
        Label type = UiTheme.Caption(TypeLine(instance));
        type.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.AddChild(type);
        band.AddChild(head);
        stack.AddChild(band);

        MarginContainer pad = UiTheme.PlateBody();
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // The first rival is the one the numbers are measured against; a second (another ring slot)
        // keeps the short delta block further down.
        bool comparing = rivals.Count > 0;
        ItemInstance? rival = comparing ? rivals[0].Item : null;

        Control single = BuildNumbers(instance, rival, comparing);
        if (single.GetChildCount() > 0 || rival != null)
        {
            col.AddChild(single);
        }
        else
        {
            single.Free(); // a pelt has no numbers; an empty box would still take a gap
        }

        if (rival != null)
        {
            Control pair = BuildSideBySide(instance, rival, rivals[0].Slot);
            col.AddChild(pair);
            card.SetCompareViews(single, pair);
        }

        if (instance.HasAffixes)
        {
            HFlowContainer chips = UiTheme.FlowRow();
            foreach (ItemAffix affix in instance.Affixes)
            {
                chips.AddChild(UiTheme.Chip(affix.DisplayValue, UiTheme.Good));
            }

            col.AddChild(chips);
        }

        if (instance.Template is ConsumableItemResource consumable && EffectText(consumable) is { Length: > 0 } effect)
        {
            col.AddChild(UiTheme.IconLabel(
                ItemPresentation.EffectIcon(consumable.Effect), effect, CooldownText(consumable), EffectColor(consumable.Effect)));
        }

        AddFacts(col, instance);
        AddRequirement(col, instance, playerLevel);
        AddSet(col, instance, equipment);
        AddUnique(col, instance.Template.UniqueEffectId);

        for (int i = 1; i < rivals.Count; i++)
        {
            Control? delta = Comparison(instance, rivals[i].Item, rivals[i].Slot, nameSlot: true);
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

        // An item with no numbers, no facts and no flavour has an empty body; the band and footer
        // then sit together without a padded gap between them.
        if (col.GetChildCount() > 0)
        {
            pad.AddChild(col);
            stack.AddChild(pad);
        }
        else
        {
            col.Free();
            pad.Free();
        }

        PanelContainer footer = UiTheme.PlateFooter();
        HFlowContainer footerRow = UiTheme.FlowRow();
        footer.AddChild(footerRow);
        stack.AddChild(footer);
        card.SetFooter(
            footerRow,
            Loc.TF("item.detail.carry", instance.Weight.ToString("0.0"), instance.Value),
            actions);

        card.AddChild(stack);
        return card;
    }

    /// <summary>"Rare  ·  Weapon  ·  Sword  ·  Two-handed": the rarity as a word, the category, and
    /// what kind of gear it is.</summary>
    private static string TypeLine(ItemInstance instance)
    {
        var parts = new List<string> { Loc.T(RarityKey(instance.Rarity)), Loc.T(TypeKey(instance.Type)) };
        if (instance.Equippable is { } gear)
        {
            if (gear.WeaponClass != WeaponClass.None)
            {
                parts.Add(Loc.T("item.weapon_class." + gear.WeaponClass.ToString().ToLowerInvariant()));
            }

            if (gear.ArmorWeight != ArmorWeight.None)
            {
                parts.Add(Loc.T("item.armor_weight." + gear.ArmorWeight.ToString().ToLowerInvariant()));
            }

            if (gear.TwoHanded)
            {
                parts.Add(Loc.T("item.two_handed"));
            }
        }

        return string.Join("   ·   ", parts);
    }

    /// <summary>
    /// The one-item view of the numbers: the hero number with its change against what is worn, then
    /// a row per stat with an arrow and a signed delta. The hero's own stat is not repeated as a row.
    /// </summary>
    private static VBoxContainer BuildNumbers(ItemInstance instance, ItemInstance? rival, bool comparing)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        ItemPresentation.HeroNumber hero = ItemPresentation.HeroOf(instance);
        if (hero.Kind != ItemPresentation.HeroKind.None)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
            row.AddChild(UiTheme.Display(Number(hero.Value), UiTheme.Text));

            var side = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            side.AddThemeConstantOverride("separation", 0);
            side.AddChild(UiTheme.Caption(Loc.T(HeroKey(hero.Kind))));
            ItemPresentation.HeroNumber? wornHero = rival is null ? null : ItemPresentation.HeroOf(rival);
            if (comparing && ItemPresentation.HeroDelta(hero, wornHero) is { } change)
            {
                side.AddChild(DeltaLine(
                    change,
                    Loc.T(rival is null ? "item.vs_empty" : "item.vs_equipped")));
            }

            row.AddChild(side);
            box.AddChild(row);
        }

        var grid = new GridContainer { Columns = comparing ? 3 : 2 };
        grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceSm);
        grid.AddThemeConstantOverride("v_separation", UiTheme.LineGap);
        foreach (ItemPresentation.StatRow row in ItemPresentation.StatRows(instance, rival, comparing))
        {
            if (hero.Kind == ItemPresentation.HeroKind.Armor && row.Stat == StatType.Armor)
            {
                continue;
            }

            grid.AddChild(StatName(row.Stat));
            Label value = UiTheme.Body(StatsPresentation.FormatDelta(row.Stat, row.Value));
            value.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(value);
            if (comparing)
            {
                grid.AddChild(row.Delta == 0f ? new Control() : DeltaLine(row.Delta, null, row.Stat));
            }
        }

        if (grid.GetChildCount() > 0)
        {
            box.AddChild(grid);
        }
        else
        {
            grid.Free();
        }

        return box;
    }

    /// <summary>
    /// The side-by-side view: what is worn in one column and this item in the next, row for row, with
    /// the arrow on this item's side wherever the two differ.
    ///
    /// Two equal columns, and each stat's name on its own quiet line over its pair of values. It was
    /// a three-column grid (name, worn, this), and three columns do not fit a card: at 1280 the names
    /// trimmed to "Arcane Re..." and "Arrows of th...", and on a handheld the name column was 26 px.
    /// Stacked, every name is whole at any width the card is given, the two item names wrap under
    /// their headings, and the values still line up down the card. It takes no more width than the
    /// one-item view, so flipping to it never reflows the screen around the card.
    /// </summary>
    private static VBoxContainer BuildSideBySide(ItemInstance instance, ItemInstance rival, EquipmentSlot slot)
    {
        var table = new VBoxContainer { Visible = false };
        table.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        table.AddChild(UiTheme.Caption(EquipmentSlots.Label(slot)));
        table.AddChild(ComparedPair(
            ComparedHeading(Loc.T("item.detail.worn"), rival),
            ComparedHeading(Loc.T("item.detail.this"), instance)));
        table.AddChild(UiTheme.RowRule());

        ItemPresentation.HeroNumber hero = ItemPresentation.HeroOf(instance);
        ItemPresentation.HeroNumber wornHero = ItemPresentation.HeroOf(rival);
        if (hero.Kind != ItemPresentation.HeroKind.None && ItemPresentation.HeroDelta(hero, wornHero) is { } change)
        {
            table.AddChild(ComparedRow(
                Loc.T(HeroKey(hero.Kind)), UiTheme.Body(Number(wornHero.Value)), ComparedValue(Number(hero.Value), change)));
        }

        foreach (ItemPresentation.StatRow row in ItemPresentation.StatRows(instance, rival, comparing: true))
        {
            if (hero.Kind == ItemPresentation.HeroKind.Armor && row.Stat == StatType.Armor)
            {
                continue;
            }

            table.AddChild(ComparedRow(
                StatNames.Label(row.Stat),
                UiTheme.Body(StatsPresentation.FormatDelta(row.Stat, row.Worn)),
                ComparedValue(StatsPresentation.FormatDelta(row.Stat, row.Value), row.Delta)));
        }

        return table;
    }

    /// <summary>Two cells of equal width: the worn column and this item's.</summary>
    private static HBoxContainer ComparedPair(Control worn, Control candidate)
    {
        var pair = new HBoxContainer();
        pair.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        worn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        candidate.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pair.AddChild(worn);
        pair.AddChild(candidate);
        return pair;
    }

    /// <summary>A column's heading: which side it is, and the item's whole name under that.</summary>
    private static VBoxContainer ComparedHeading(string side, ItemInstance item)
    {
        var heading = new VBoxContainer();
        heading.AddThemeConstantOverride("separation", 0);
        heading.AddChild(UiTheme.Caption(side, UiTheme.Accent));
        heading.AddChild(ComparedName(item));
        return heading;
    }

    /// <summary>One compared stat: its name on a quiet line, then the two values under the columns.</summary>
    private static VBoxContainer ComparedRow(string name, Control worn, Control candidate)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        Label label = UiTheme.Caption(name);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        row.AddChild(ComparedPair(worn, candidate));
        return row;
    }

    /// <summary>A stat's name in the one-item view's grid. It wraps onto a second line when the
    /// column is narrow ("Arcane / Resistance"): a trimmed "Arcane Re..." beside a number says
    /// nothing, and a pad has no pointer to hover for the tooltip. No minimum width of its own, so a
    /// narrow card is never pushed wider by it.</summary>
    private static Label StatName(StatType stat)
    {
        Label label = UiTheme.Body(StatNames.Label(stat), UiTheme.Dim);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    /// <summary>An item's name over its column of the side-by-side view, whole: it wraps under
    /// itself instead of trimming, since two rolled names that differ only at the end ("...of the
    /// Thaw", "...of Warding") trim to the same word.</summary>
    private static Label ComparedName(ItemInstance item)
    {
        Label label = UiTheme.Caption(item.DisplayName, UiTheme.RarityColor(item.Rarity));
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.TooltipText = $"{item.DisplayName} ({Loc.T(RarityKey(item.Rarity))})";
        return label;
    }

    private static HBoxContainer ComparedValue(string text, float delta)
    {
        var cell = new HBoxContainer();
        cell.AddThemeConstantOverride("separation", UiTheme.Space2xs);
        cell.AddChild(UiTheme.Body(text));
        if (delta != 0f)
        {
            cell.AddChild(UiTheme.DeltaArrow(delta));
        }

        return cell;
    }

    /// <summary>An arrow, a signed number in the gain or loss colour, and an optional quiet note after
    /// it. The sign and the arrow both say the direction; the colour only agrees with them.</summary>
    private static HBoxContainer DeltaLine(float delta, string? note, StatType? stat = null)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", UiTheme.Space2xs);
        if (Mathf.Abs(delta) < 0.0001f)
        {
            line.AddChild(UiTheme.Caption(Loc.T("item.detail.same")));
            return line;
        }

        line.AddChild(UiTheme.DeltaArrow(delta));
        string text = stat is { } named ? StatsPresentation.FormatDelta(named, delta) : Signed(delta);
        line.AddChild(UiTheme.Caption(text, delta > 0f ? UiTheme.Good : UiTheme.Bad));
        if (!string.IsNullOrEmpty(note))
        {
            // Trimmed rather than allowed to set the card's width: at a large text size the note is
            // the first thing to give, and the arrow and the number have already said it.
            Label quiet = UiTheme.Caption(note);
            quiet.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            quiet.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            quiet.TooltipText = note;
            line.AddChild(quiet);
        }

        return line;
    }

    private static string Number(float value) => value.ToString("0.#");

    private static string Signed(float value) => (value > 0f ? "+" : string.Empty) + value.ToString("0.#");

    private static string HeroKey(ItemPresentation.HeroKind kind) => kind switch
    {
        ItemPresentation.HeroKind.Damage => "item.hero.damage",
        ItemPresentation.HeroKind.Armor => "item.hero.armor",
        ItemPresentation.HeroKind.Stamina => "item.hero.stamina",
        ItemPresentation.HeroKind.Mana => "item.hero.mana",
        _ => "item.hero.health",
    };

    /// <summary>The per-copy facts as one row of chips: item level, workmanship, upgrade level and
    /// the player's own marks. Nothing is shown for a fact at its default, so a plain potion's card
    /// does not grow a row of zeroes. What kind of gear it is lives in the type line on the band.</summary>
    private static void AddFacts(VBoxContainer col, ItemInstance instance)
    {
        HFlowContainer chips = UiTheme.FlowRow();

        if (instance.EffectiveItemLevel > 0)
        {
            chips.AddChild(UiTheme.Chip(Loc.TF("item.level", instance.EffectiveItemLevel), UiTheme.Text));
        }

        if (instance.Quality != CraftQuality.Standard)
        {
            chips.AddChild(UiTheme.Chip(Loc.T(QualityKey(instance.Quality)), QualityColor(instance.Quality)));
        }

        if (instance.UpgradeLevel > 0)
        {
            chips.AddChild(UiTheme.Chip(Loc.TF("item.upgrade", instance.UpgradeLevel, ItemUpgrades.MaxLevel), UiTheme.Good));
        }

        if (instance.Locked)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("item.locked"), UiTheme.Accent));
        }

        if (instance.Junk)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("item.junk"), UiTheme.Dim));
        }

        if (chips.GetChildCount() > 0)
        {
            col.AddChild(chips);
        }
        else
        {
            chips.Free();
        }
    }

    /// <summary>The level requirement. Unmet, it is red and says both numbers: the colour is the
    /// glance, and "you are level 12" is what survives a colour-vision mode.</summary>
    private static void AddRequirement(VBoxContainer col, ItemInstance instance, int playerLevel)
    {
        int required = instance.Template.RequiredLevel;
        if (required <= 0)
        {
            return;
        }

        bool met = playerLevel <= 0 || ItemPresentation.MeetsLevel(required, playerLevel);
        col.AddChild(met
            ? UiTheme.Caption(Loc.TF("item.requires_level", required))
            : UiTheme.Caption(Loc.TF("item.requires_level_unmet", required, playerLevel), UiTheme.Bad));
    }

    /// <summary>The set block: the set's name with how many pieces are worn, then one line per
    /// bonus. A bonus that is on says "active" as well as lighting up, so the state is in the text
    /// and not only in the colour.</summary>
    private static void AddSet(VBoxContainer col, ItemInstance instance, EquipmentComponent? equipment)
    {
        if (ItemSetDatabase.Get(instance.Template.SetId) is not { } set)
        {
            return;
        }

        // The count the equipment itself applies the bonuses by, so a line here is lit exactly
        // when its modifier is on the wearer.
        int worn = equipment?.SetPiecesWorn(set.Id) ?? 0;

        col.AddChild(UiTheme.Divider());
        string setName = Loc.Has(set.DisplayNameKey) ? Loc.T(set.DisplayNameKey) : set.Id;
        col.AddChild(UiTheme.Body(Loc.TF("item.set_header", setName, worn, set.PieceIds.Count), UiTheme.Accent));

        foreach (ItemSetBonusResource bonus in set.Bonuses.Where(b => b != null).OrderBy(b => b.PiecesRequired))
        {
            var parts = new List<string>();
            if (bonus.HasStat)
            {
                bool percent = bonus.ModifierType != ModifierType.Flat || bonus.Stat == StatType.CritChance;
                parts.Add($"{ItemPresentation.BonusNumber(bonus.Value, percent)} {StatNames.Label(bonus.Stat)}");
            }

            if (bonus.HasEffect && UniqueEffectDatabase.Get(bonus.EffectId) is { } effect)
            {
                parts.Add($"{UniqueName(effect)}: {UniqueDescription(effect)}");
            }

            if (parts.Count == 0)
            {
                continue;
            }

            bool lit = worn >= bonus.PiecesRequired;
            Label line = UiTheme.Caption(
                Loc.TF(lit ? "item.set_bonus_on" : "item.set_bonus_off", bonus.PiecesRequired, string.Join(", ", parts)),
                lit ? UiTheme.Good : UiTheme.Disabled);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(line);
        }
    }

    private static void AddUnique(VBoxContainer col, string effectId)
    {
        if (UniqueEffectDatabase.Get(effectId) is not { } effect)
        {
            return;
        }

        col.AddChild(UiTheme.Divider());
        col.AddChild(UiTheme.Body(UniqueName(effect), UiTheme.Accent));

        Label text = UiTheme.Caption(UniqueDescription(effect), UiTheme.Text);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(text);
    }

    private static string UniqueName(UniqueEffectResource effect) =>
        Loc.Has(effect.NameKey) ? Loc.T(effect.NameKey) : effect.Id;

    private static string UniqueDescription(UniqueEffectResource effect)
    {
        if (!Loc.Has(effect.DescriptionKey))
        {
            return string.Empty;
        }

        object[] args = ItemPresentation.UniqueArgs(
            effect.Kind, effect.Magnitude, effect.Chance, effect.Threshold, effect.DurationSeconds, effect.CooldownSeconds);
        return Loc.TF(effect.DescriptionKey, args);
    }

    /// <summary>
    /// What using a consumable does, in one line, or empty when it does nothing worth a line.
    /// Shared by the detail card and the hotbar's tooltip so the two cannot describe one potion
    /// two ways.
    /// </summary>
    public static string EffectText(ConsumableItemResource consumable)
    {
        string length = ItemPresentation.Seconds(consumable.DurationSeconds);
        bool overTime = consumable.DurationSeconds > 0f;

        switch (consumable.Effect)
        {
            case ConsumableEffectKind.RestoreStamina:
                return consumable.Magnitude <= 0f ? string.Empty
                    : overTime ? Loc.TF("item.effect.stamina_over", Amount(consumable.Magnitude), length)
                    : Loc.TF("item.effect.stamina", Amount(consumable.Magnitude));

            case ConsumableEffectKind.RestoreMana:
                return consumable.Magnitude <= 0f ? string.Empty
                    : overTime ? Loc.TF("item.effect.mana_over", Amount(consumable.Magnitude), length)
                    : Loc.TF("item.effect.mana", Amount(consumable.Magnitude));

            case ConsumableEffectKind.Buff:
                bool percent = consumable.BuffKind != ModifierType.Flat || consumable.BuffStat == StatType.CritChance;
                string bonus = ItemPresentation.BonusNumber(consumable.Magnitude, percent);
                return overTime
                    ? Loc.TF("item.effect.buff", bonus, StatNames.Label(consumable.BuffStat), length)
                    : Loc.TF("item.effect.buff_instant", bonus, StatNames.Label(consumable.BuffStat));

            case ConsumableEffectKind.Cure:
                if (consumable.CureStatusIds.Count == 0)
                {
                    return Loc.T("item.effect.cure_all");
                }

                var names = new List<string>();
                foreach (string id in consumable.CureStatusIds)
                {
                    names.Add(StatusEffectDatabase.Get(id)?.LocalName ?? id);
                }

                return Loc.TF("item.effect.cure", string.Join(", ", names));

            default:
                float heal = consumable.EffectiveHeal;
                return heal <= 0f ? string.Empty
                    : overTime ? Loc.TF("item.effect.heal_over", Amount(heal), length)
                    : Loc.TF("item.effect.heal", Amount(heal));
        }
    }

    /// <summary>The cooldown line under a consumable's effect, or null when it has none. A shared
    /// group is named, because "the other potion is greyed out too" is otherwise a surprise.</summary>
    public static string? CooldownText(ConsumableItemResource consumable)
    {
        if (consumable.CooldownSeconds <= 0f)
        {
            return null;
        }

        string wait = ItemPresentation.Seconds(consumable.CooldownSeconds);
        string groupKey = "item.cooldown_group." + consumable.CooldownGroup;
        return consumable.CooldownGroup.Length > 0 && Loc.Has(groupKey)
            ? Loc.TF("item.cooldown_shared", wait, Loc.T(groupKey))
            : Loc.TF("item.cooldown", wait);
    }

    /// <summary>The colour of a consumable effect's icon: the vital it restores, or the accent.</summary>
    public static Color EffectColor(ConsumableEffectKind effect) => effect switch
    {
        ConsumableEffectKind.Heal => UiTheme.Health,
        ConsumableEffectKind.RestoreStamina => UiTheme.Stamina,
        ConsumableEffectKind.RestoreMana => UiTheme.Mana,
        ConsumableEffectKind.Cure => UiTheme.Good,
        _ => UiTheme.Accent,
    };

    private static string Amount(float value) => value.ToString("0.#");

    private static string QualityKey(CraftQuality quality) => quality switch
    {
        CraftQuality.Fine => "item.quality.fine",
        CraftQuality.Superior => "item.quality.superior",
        CraftQuality.Masterwork => "item.quality.masterwork",
        _ => "item.quality.standard",
    };

    private static Color QualityColor(CraftQuality quality) => quality switch
    {
        CraftQuality.Masterwork => UiTheme.Accent,
        CraftQuality.Superior => UiTheme.GlyphLight,
        _ => UiTheme.Good,
    };

    /// <summary>
    /// The equipped-vs-candidate stat block, or null when the two are identical (in which case
    /// there is nothing to say and a "no change" line would be clutter).
    ///
    /// Every row is prefixed with +/- as well as coloured, so the comparison survives the 37.5G
    /// colourblind modes — this is the one place in the UI where getting a colour wrong means
    /// equipping the worse item.
    ///
    /// <paramref name="nameSlot"/> is set when there is more than one block (the rings), so each
    /// says which slot it is measuring against.
    /// </summary>
    private static Control? Comparison(ItemInstance candidate, ItemInstance? equipped, EquipmentSlot slot, bool nameSlot)
    {
        var deltas = ItemPresentation.Compare(candidate, equipped);
        if (deltas.Count == 0)
        {
            return null;
        }

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        string caption = !nameSlot
            ? Loc.T(equipped is null ? "item.vs_empty" : "item.vs_equipped")
            : equipped is null
                ? Loc.TF("item.vs_slot_empty", EquipmentSlots.Label(slot))
                : Loc.TF("item.vs_slot", EquipmentSlots.Label(slot), equipped.DisplayName);
        Label head = UiTheme.Caption(caption);
        head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(head);

        foreach ((StatType stat, float delta) in deltas)
        {
            bool up = delta > 0f;
            col.AddChild(UiTheme.Caption(
                $"{(up ? "+" : "−")} {StatNames.Label(stat)}  {delta:+0.##;-0.##}",
                up ? UiTheme.Good : UiTheme.Bad));
        }

        return col;
    }

    private static string Tooltip(ItemInstance instance, int quantity, Marks marks)
    {
        string count = quantity > 1 ? $" ×{quantity}" : string.Empty;
        string mark = instance.Locked ? $"  ({Loc.T("item.locked")})"
            : instance.Junk ? $"  ({Loc.T("item.junk")})"
            : string.Empty;
        string worn = (marks & Marks.Equipped) != 0 ? $"  ({Loc.T("item.equipped")})" : string.Empty;
        string fresh = (marks & Marks.New) != 0 ? $"  ({Loc.T("item.new")})" : string.Empty;
        return $"{instance.DisplayName}{count}  ({Loc.T(RarityKey(instance.Rarity))}){mark}{worn}{fresh}";
    }

    /// <summary>The <c>Loc</c> key for a rarity's name. Rarity is always available as a word (the type
    /// line of the detail card, a slot's tooltip), which is the channel no colour setting can take away.</summary>
    public static string RarityKey(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Uncommon => "item.rarity.uncommon",
        ItemRarity.Rare => "item.rarity.rare",
        ItemRarity.Epic => "item.rarity.epic",
        ItemRarity.Legendary => "item.rarity.legendary",
        _ => "item.rarity.common",
    };

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
