using System.Collections.Generic;
using System.Text;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// A concrete, in-world copy of an item: an <see cref="ItemResource"/> template
/// plus the per-instance state that loot generation produces — a rolled
/// <see cref="Rarity"/>, a generated <see cref="DisplayName"/>, and a frozen list
/// of <see cref="ItemAffix"/>es. Mundane items (potions, materials, gold) are
/// represented as plain instances with no affixes via <see cref="Plain"/>, so the
/// rest of the game only ever deals in instances.
///
/// Stacking: only affix-less instances of the same template stack together; any
/// rolled item is unique and occupies its own slot.
/// </summary>
public sealed class ItemInstance
{
    /// <summary>Save keys of the per-instance state below. Additive: each is written only when it
    /// differs from its default and reads back as the default when absent.</summary>
    public const string QualityKey = "quality";
    public const string UpgradeKey = "upgrade";
    public const string ItemLevelKey = "ilvl";
    public const string LockedKey = "locked";
    public const string JunkKey = "junk";

    private static readonly IReadOnlyList<ItemAffix> NoAffixes = new List<ItemAffix>();

    private int _upgradeLevel;

    public ItemInstance(ItemResource template, ItemRarity? rarity = null,
        IReadOnlyList<ItemAffix>? affixes = null, string? displayName = null)
    {
        Template = template;
        Affixes = affixes ?? NoAffixes;
        Rarity = rarity ?? template.Rarity;
        DisplayName = displayName ?? (Affixes.Count > 0 ? BuildName(template, Affixes) : template.DisplayName);
    }

    public ItemResource Template { get; }

    public ItemRarity Rarity { get; }

    public IReadOnlyList<ItemAffix> Affixes { get; }

    public string DisplayName { get; }

    // --- Per-instance state added by ics-base. Every one is absent-default (Standard / 0 / false),
    // is saved only when it differs from that default, and reads back as the default from a save
    // that predates it. ---

    /// <summary>The workmanship frozen on at the bench. Loot is always
    /// <see cref="CraftQuality.Standard"/>. What it is worth lives in <see cref="CraftQualities"/>.</summary>
    public CraftQuality Quality { get; set; }

    /// <summary>How many times this copy has been upgraded, clamped to
    /// <c>0..ItemUpgrades.MaxLevel</c>. What a level is worth lives in <see cref="ItemUpgrades"/>.</summary>
    public int UpgradeLevel
    {
        get => _upgradeLevel;
        set => _upgradeLevel = ItemUpgrades.Clamp(value);
    }

    /// <summary>The item level this copy was rolled at. 0 means it was never rolled at a level, in
    /// which case <see cref="EffectiveItemLevel"/> falls back to the template's.</summary>
    public int ItemLevel { get; set; }

    /// <summary>The rolled <see cref="ItemLevel"/>, or the template's authored
    /// <see cref="ItemResource.ItemLevel"/> for a copy that was never rolled at one.</summary>
    public int EffectiveItemLevel => ItemLevel > 0 ? ItemLevel : Template.ItemLevel;

    /// <summary>Player-set guard: a locked item is never sold, salvaged, dropped or marked junk. The
    /// flag is only storage here; change it through <see cref="InventoryComponent.SetLocked"/> so the
    /// inventory announces it, and enforce it at the action that would lose the item.</summary>
    public bool Locked { get; set; }

    /// <summary>Player-set mark for "sell or scrap this in bulk". Change it through
    /// <see cref="InventoryComponent.SetJunk"/>; a locked item is never junk.</summary>
    public bool Junk { get; set; }

    public string TemplateId => Template.Id;

    public ItemType Type => Template.Type;

    public float Weight => Template.Weight;

    public bool HasAffixes => Affixes.Count > 0;

    /// <summary>Affix-less items stack per their template; rolled items never do.</summary>
    public bool IsStackable => Template.IsStackable && !HasAffixes;

    public int MaxStack => IsStackable ? Template.MaxStack : 1;

    public EquippableItemResource? Equippable => Template as EquippableItemResource;

    public bool IsEquippable => Template is EquippableItemResource;

    /// <summary>The value hook for workmanship: <see cref="CraftQualities.ValueMultiplier"/> times
    /// <see cref="ItemUpgrades.ValueMultiplier"/>. Exactly 1 until the crafting lane tunes those two,
    /// so <see cref="Value"/> is unchanged today.</summary>
    public float WorkmanshipValueMultiplier =>
        CraftQualities.ValueMultiplier(Quality) * ItemUpgrades.ValueMultiplier(UpgradeLevel);

    /// <summary>The stat hook for workmanship, applied to the template's flat bonuses only (rolled
    /// affixes are never scaled). Exactly 1 today, like <see cref="WorkmanshipValueMultiplier"/>.</summary>
    public float WorkmanshipStatMultiplier =>
        CraftQualities.StatMultiplier(Quality) * ItemUpgrades.StatMultiplier(UpgradeLevel);

    /// <summary>Merchant value: the template value plus a modest premium per affix,
    /// scaled by rarity and by <see cref="WorkmanshipValueMultiplier"/>.</summary>
    public int Value
    {
        get
        {
            float rarityMult = 1f + ((int)Rarity * 0.5f);
            return Mathf.RoundToInt((Template.Value + (Affixes.Count * 10)) * rarityMult * WorkmanshipValueMultiplier);
        }
    }

    /// <summary>Two instances stack only when both are affix-less copies of the same stackable
    /// template with the same workmanship and level. <see cref="Locked"/> and <see cref="Junk"/> are
    /// deliberately not compared: a fresh pickup joins the stack the player already marked.</summary>
    public bool CanStackWith(ItemInstance other)
    {
        return IsStackable && other.IsStackable && other.TemplateId == TemplateId
            && other.Quality == Quality && other.UpgradeLevel == UpgradeLevel && other.ItemLevel == ItemLevel;
    }

    /// <summary>Wraps a template as a plain, affix-less instance (mundane items).</summary>
    public static ItemInstance Plain(ItemResource template) => new(template);

    /// <summary>A separate instance with the same template, roll and per-instance state. Used when
    /// one stack becomes two, so each has its own identity (stacks are found by reference).</summary>
    public ItemInstance Copy()
    {
        return new ItemInstance(Template, Rarity, Affixes, DisplayName)
        {
            Quality = Quality,
            UpgradeLevel = UpgradeLevel,
            ItemLevel = ItemLevel,
            Locked = Locked,
            Junk = Junk,
        };
    }

    /// <summary>
    /// Combined stat bonuses this instance grants while equipped: the equippable
    /// template's flat bonuses (times <see cref="WorkmanshipStatMultiplier"/>) followed by
    /// every rolled affix.
    /// </summary>
    public IEnumerable<(StatType Stat, float Value, ModifierType Type)> StatBonuses()
    {
        if (Template is EquippableItemResource equippable)
        {
            float workmanship = WorkmanshipStatMultiplier;
            foreach ((StatType stat, float value) in equippable.StatBonuses())
            {
                yield return (stat, value * workmanship, ModifierType.Flat);
            }
        }

        foreach (ItemAffix affix in Affixes)
        {
            yield return (affix.Stat, affix.Value, affix.ModifierType);
        }
    }

    private static string BuildName(ItemResource template, IReadOnlyList<ItemAffix> affixes)
    {
        string? prefix = null;
        string? suffix = null;
        foreach (ItemAffix affix in affixes)
        {
            if (affix.Kind == AffixKind.Prefix && prefix == null)
            {
                prefix = affix.Label;
            }
            else if (affix.Kind == AffixKind.Suffix && suffix == null)
            {
                suffix = affix.Label;
            }
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(prefix))
        {
            sb.Append(prefix).Append(' ');
        }

        sb.Append(template.DisplayName);
        if (!string.IsNullOrEmpty(suffix))
        {
            sb.Append(' ').Append(suffix);
        }

        return sb.ToString();
    }

    // --- Persistence --------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var affixes = new Godot.Collections.Array();
        foreach (ItemAffix affix in Affixes)
        {
            affixes.Add(affix.Save());
        }

        var data = new Godot.Collections.Dictionary
        {
            ["id"] = TemplateId,
            ["rarity"] = (int)Rarity,
            ["name"] = DisplayName,
            ["affixes"] = affixes,
        };

        // Written only off their default, so a save holding none of them is exactly what it was
        // before these keys existed.
        if (Quality != CraftQuality.Standard)
        {
            data[QualityKey] = (int)Quality;
        }

        if (UpgradeLevel != 0)
        {
            data[UpgradeKey] = UpgradeLevel;
        }

        if (ItemLevel != 0)
        {
            data[ItemLevelKey] = ItemLevel;
        }

        if (Locked)
        {
            data[LockedKey] = true;
        }

        if (Junk)
        {
            data[JunkKey] = true;
        }

        return data;
    }

    /// <summary>Rebuilds an instance from saved state, resolving the template via
    /// the <see cref="ItemDatabase"/>. Returns null if the template is gone or the entry has no id.
    /// Every read is tolerant (<see cref="SaveRead"/>): an absent or mistyped key takes its default
    /// and an unreadable affix is dropped, so one bad item never fails the load around it.</summary>
    public static ItemInstance? FromSave(Godot.Collections.Dictionary? data)
    {
        string id = SaveRead.Text(data, "id");
        ItemResource? template = string.IsNullOrEmpty(id) ? null : ItemDatabase.Get(id);
        if (data == null || template == null)
        {
            return null;
        }

        int savedRarity = SaveRead.Int(data, "rarity", (int)template.Rarity);
        ItemRarity rarity = System.Enum.IsDefined(typeof(ItemRarity), savedRarity)
            ? (ItemRarity)savedRarity
            : template.Rarity;
        string? name = data.ContainsKey("name") ? SaveRead.Text(data, "name") : null;
        if (string.IsNullOrEmpty(name))
        {
            name = null;
        }

        var affixes = new List<ItemAffix>();
        foreach (Variant entry in SaveRead.List(data, "affixes"))
        {
            if (SaveRead.AsSection(entry) is { } affixData && ItemAffix.FromSave(affixData) is { } affix)
            {
                affixes.Add(affix);
            }
        }

        return new ItemInstance(template, rarity, affixes, name)
        {
            Quality = CraftQualities.FromOrdinal(SaveRead.Int(data, QualityKey)),
            UpgradeLevel = SaveRead.Int(data, UpgradeKey),
            ItemLevel = System.Math.Max(0, SaveRead.Int(data, ItemLevelKey)),
            Locked = SaveRead.Flag(data, LockedKey),
            Junk = SaveRead.Flag(data, JunkKey),
        };
    }
}
