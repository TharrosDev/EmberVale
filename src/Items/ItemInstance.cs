using System.Collections.Generic;
using System.Text;
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
    private static readonly IReadOnlyList<ItemAffix> NoAffixes = new List<ItemAffix>();

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

    public string TemplateId => Template.Id;

    public ItemType Type => Template.Type;

    public float Weight => Template.Weight;

    public bool HasAffixes => Affixes.Count > 0;

    /// <summary>Affix-less items stack per their template; rolled items never do.</summary>
    public bool IsStackable => Template.IsStackable && !HasAffixes;

    public int MaxStack => IsStackable ? Template.MaxStack : 1;

    public EquippableItemResource? Equippable => Template as EquippableItemResource;

    public bool IsEquippable => Template is EquippableItemResource;

    /// <summary>Merchant value: the template value plus a modest premium per affix,
    /// scaled by rarity. Purely informational for now.</summary>
    public int Value
    {
        get
        {
            float rarityMult = 1f + ((int)Rarity * 0.5f);
            return Mathf.RoundToInt((Template.Value + (Affixes.Count * 10)) * rarityMult);
        }
    }

    /// <summary>Two instances stack only when both are affix-less copies of the
    /// same stackable template.</summary>
    public bool CanStackWith(ItemInstance other)
    {
        return IsStackable && other.IsStackable && other.TemplateId == TemplateId;
    }

    /// <summary>Wraps a template as a plain, affix-less instance (mundane items).</summary>
    public static ItemInstance Plain(ItemResource template) => new(template);

    /// <summary>
    /// Combined stat bonuses this instance grants while equipped: the equippable
    /// template's flat bonuses followed by every rolled affix.
    /// </summary>
    public IEnumerable<(StatType Stat, float Value, ModifierType Type)> StatBonuses()
    {
        if (Template is EquippableItemResource equippable)
        {
            foreach ((StatType stat, float value) in equippable.StatBonuses())
            {
                yield return (stat, value, ModifierType.Flat);
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

        return new Godot.Collections.Dictionary
        {
            ["id"] = TemplateId,
            ["rarity"] = (int)Rarity,
            ["name"] = DisplayName,
            ["affixes"] = affixes,
        };
    }

    /// <summary>Rebuilds an instance from saved state, resolving the template via
    /// the <see cref="ItemDatabase"/>. Returns null if the template is gone.</summary>
    public static ItemInstance? FromSave(Godot.Collections.Dictionary data)
    {
        string id = data["id"].AsString();
        ItemResource? template = ItemDatabase.Get(id);
        if (template == null)
        {
            return null;
        }

        var rarity = data.TryGetValue("rarity", out Variant rarityVar)
            ? (ItemRarity)rarityVar.AsInt32()
            : template.Rarity;
        string? name = data.TryGetValue("name", out Variant nameVar) ? nameVar.AsString() : null;

        var affixes = new List<ItemAffix>();
        if (data.TryGetValue("affixes", out Variant affixVar))
        {
            foreach (Variant entry in affixVar.AsGodotArray())
            {
                affixes.Add(ItemAffix.FromSave(entry.AsGodotDictionary()));
            }
        }

        return new ItemInstance(template, rarity, affixes, name);
    }
}
