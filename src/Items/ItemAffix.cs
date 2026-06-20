using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// A single rolled magic property carried by an <see cref="ItemInstance"/>. It is
/// produced from an <see cref="AffixDefinition"/> at generation time with a value
/// chosen inside the definition's range, then frozen on the instance so the item
/// keeps the same stats for its lifetime (and across save/load).
///
/// An affix maps directly onto a <see cref="StatModifier"/> when the item is
/// equipped — the instance is used as the modifier <c>Source</c> so every bonus
/// from one item is stripped together on unequip.
/// </summary>
public sealed class ItemAffix
{
    public ItemAffix(string id, string label, AffixKind kind, StatType stat, float value, ModifierType modifierType)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Stat = stat;
        Value = value;
        ModifierType = modifierType;
    }

    /// <summary>Id of the <see cref="AffixDefinition"/> this was rolled from.</summary>
    public string Id { get; }

    /// <summary>Name fragment shown in the generated item name (e.g. "of the Bear").</summary>
    public string Label { get; }

    public AffixKind Kind { get; }

    public StatType Stat { get; }

    public float Value { get; }

    public ModifierType ModifierType { get; }

    /// <summary>Human-readable bonus line for tooltips, e.g. "+12 Armor" / "+8% Crit Chance".</summary>
    public string DisplayValue
    {
        get
        {
            // Percentage display for multiplicative modifiers and for CritChance,
            // which is stored as a 0..1 fraction even when added flatly.
            bool percent = ModifierType != ModifierType.Flat || Stat == StatType.CritChance;
            string sign = Value >= 0f ? "+" : string.Empty;
            string number = percent ? $"{Value * 100f:0.#}%" : $"{Value:0.#}";
            return $"{sign}{number} {StatLabels.Short(Stat)}";
        }
    }

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary
        {
            ["id"] = Id,
            ["label"] = Label,
            ["kind"] = (int)Kind,
            ["stat"] = (int)Stat,
            ["value"] = Value,
            ["mod"] = (int)ModifierType,
        };
    }

    public static ItemAffix FromSave(Godot.Collections.Dictionary data)
    {
        return new ItemAffix(
            data["id"].AsString(),
            data["label"].AsString(),
            (AffixKind)data["kind"].AsInt32(),
            (StatType)data["stat"].AsInt32(),
            data["value"].AsSingle(),
            (ModifierType)data["mod"].AsInt32());
    }
}
