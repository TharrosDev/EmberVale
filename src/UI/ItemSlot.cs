using System.Collections.Generic;
using System.Linq;
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
    public sealed record DetailContext(EquipmentComponent? Equipment = null, int PlayerLevel = 0, bool Compare = false);

    /// <summary>
    /// One inventory cell: rarity frame, category glyph (or the item's <c>Icon</c> if one is ever
    /// authored), and a stack count in the corner.
    ///
    /// <paramref name="selected"/> draws the ember selection rule. It is a separate signal from
    /// focus on purpose: a controller player moves *focus* across the grid to browse, and the
    /// selected item is the one the detail pane is describing. Collapsing the two would mean the
    /// pane changed every time the stick twitched.
    ///
    /// A locked item carries a padlock in its top-left corner and a junk item a coin; an upgraded
    /// one shows its level top-right. They are icons and a number, not tints, so the marks read
    /// under every colour-vision mode.
    /// </summary>
    public static Button Build(ItemInstance? instance, int quantity = 1, bool selected = false, float size = DefaultSize)
    {
        var slot = new Button
        {
            CustomMinimumSize = new Vector2(size, size),

            // Not Flat: a flat button never draws its `normal` stylebox, so the rarity frame and the
            // empty well were skipped entirely and every slot read as a loose glyph with no edge.
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
            count.OffsetRight = -UiTheme.Space2xs;
            count.OffsetBottom = -UiTheme.Space2xs;
            slot.AddChild(count);
        }

        if (instance.Locked || instance.Junk)
        {
            float badge = Mathf.Max(12f, size * 0.3f);
            TextureRect mark = instance.Locked
                ? UiIcon.Create(UiIcon.Kind.Lock, badge, UiTheme.Accent)
                : UiIcon.Create(UiIcon.Kind.Currency, badge, UiTheme.Dim);
            mark.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            mark.OffsetLeft = 2f;
            mark.OffsetTop = 2f;
            mark.OffsetRight = 2f + badge;
            mark.OffsetBottom = 2f + badge;
            slot.AddChild(mark);
        }

        if (instance.UpgradeLevel > 0)
        {
            Label plus = UiTheme.Caption($"+{instance.UpgradeLevel}", UiTheme.Good);
            plus.HorizontalAlignment = HorizontalAlignment.Right;
            plus.MouseFilter = Control.MouseFilterEnum.Ignore;
            plus.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            plus.OffsetRight = -UiTheme.Space2xs;
            slot.AddChild(plus);
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
        var rivals = new List<(EquipmentSlot Slot, ItemInstance? Item)>();
        if (compare && instance.Equippable is { } gear)
        {
            rivals.Add((gear.Slot, equipped));
        }

        return BuildDetail(instance, rivals, null, 0);
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

        return BuildDetail(instance, rivals, context.Equipment, context.PlayerLevel);
    }

    private static Control BuildDetail(
        ItemInstance instance,
        IReadOnlyList<(EquipmentSlot Slot, ItemInstance? Item)> rivals,
        EquipmentComponent? equipment,
        int playerLevel)
    {
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label name = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        UiTheme.ApplyType(name, UiTheme.FontRole.Display, UiTheme.HeaderFontSize);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(name);

        col.AddChild(UiTheme.Caption(Loc.TF(
            "item.meta",
            Loc.T(TypeKey(instance.Type)),
            instance.Weight.ToString("0.0"),
            instance.Value)));

        AddFacts(col, instance);
        AddRequirement(col, instance, playerLevel);

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

        AddSet(col, instance, equipment);
        AddUnique(col, instance.Template.UniqueEffectId);

        foreach ((EquipmentSlot slot, ItemInstance? rival) in rivals)
        {
            Control? delta = Comparison(instance, rival, slot, rivals.Count > 1);
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

        card.AddChild(col); // the card's own margins are the padding; a second pad doubled the left edge
        return card;
    }

    /// <summary>The per-copy facts as one row of chips: item level, workmanship, upgrade level and
    /// the player's own marks, then what kind of gear it is. Nothing is shown for a fact at its
    /// default, so a plain potion's card does not grow a row of zeroes.</summary>
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

        if (instance.Equippable is not { } gear)
        {
            return;
        }

        var parts = new List<string>();
        if (gear.TwoHanded)
        {
            parts.Add(Loc.T("item.two_handed"));
        }

        if (gear.WeaponClass != WeaponClass.None)
        {
            parts.Add(Loc.T("item.weapon_class." + gear.WeaponClass.ToString().ToLowerInvariant()));
        }

        if (gear.ArmorWeight != ArmorWeight.None)
        {
            parts.Add(Loc.T("item.armor_weight." + gear.ArmorWeight.ToString().ToLowerInvariant()));
        }

        if (parts.Count > 0)
        {
            col.AddChild(UiTheme.Caption(string.Join("   ·   ", parts)));
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

        int worn = equipment is null
            ? 0
            : ItemPresentation.WornPieces(set.PieceIds, equipment.EquippedInstances.Select(i => i.TemplateId));

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

    private static string Tooltip(ItemInstance instance, int quantity)
    {
        string count = quantity > 1 ? $" ×{quantity}" : string.Empty;
        string mark = instance.Locked ? $"  ({Loc.T("item.locked")})"
            : instance.Junk ? $"  ({Loc.T("item.junk")})"
            : string.Empty;
        return $"{instance.DisplayName}{count}{mark}";
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
