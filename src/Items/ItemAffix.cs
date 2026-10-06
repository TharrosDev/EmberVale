using Embervale.Localization;
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
/// from one item is stripped together on unequip. The exception is a regeneration affix
/// (<see cref="Effect"/> other than <see cref="AffixEffect.Stat"/>): its number is a rate, not a
/// stat, and is read through <see cref="Magnitude"/>.
/// </summary>
public sealed class ItemAffix
{
    /// <summary>Save key of <see cref="Effect"/>; written only for a non-stat affix and read as
    /// <see cref="AffixEffect.Stat"/> when absent, so every older save loads unchanged.</summary>
    public const string EffectKey = "fx";

    public ItemAffix(string id, string label, AffixKind kind, StatType stat, float value, ModifierType modifierType,
        AffixEffect effect = AffixEffect.Stat)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Stat = stat;
        Magnitude = value;
        ModifierType = modifierType;
        Effect = effect;
    }

    /// <summary>Id of the <see cref="AffixDefinition"/> this was rolled from.</summary>
    public string Id { get; }

    /// <summary>Name fragment shown in the generated item name (e.g. "of the Bear").</summary>
    public string Label { get; }

    public AffixKind Kind { get; }

    public StatType Stat { get; }

    /// <summary>What the number does (see <see cref="AffixEffect"/>).</summary>
    public AffixEffect Effect { get; }

    /// <summary>The rolled number, whatever it applies to.</summary>
    public float Magnitude { get; }

    /// <summary>
    /// The amount this affix adds to <see cref="Stat"/> as a modifier. It is <see cref="Magnitude"/>
    /// for a stat affix and <b>zero</b> for a regeneration affix: everything that turns an item's
    /// affixes into stat modifiers reads this, and a regen rate applied as a modifier on the
    /// resource it names would silently become bonus maximum health or mana.
    /// </summary>
    public float Value => Effect == AffixEffect.Stat ? Magnitude : 0f;

    public ModifierType ModifierType { get; }

    /// <summary>Human-readable bonus line for tooltips, e.g. "+12 Armor" / "+8% Crit Chance" /
    /// "+1.5 Mana Regeneration".</summary>
    public string DisplayValue
    {
        get
        {
            string sign = Magnitude >= 0f ? "+" : string.Empty;
            if (Effect != AffixEffect.Stat)
            {
                return Loc.TF(EffectKeyFor(Effect), $"{sign}{Magnitude:0.#}");
            }

            // Percentage display for multiplicative modifiers, for CritChance (stored as a 0..1
            // fraction even when added flatly), and for any definition that says its value is one.
            bool percent = ModifierType != ModifierType.Flat || Stat == StatType.CritChance
                || AffixDatabase.Get(Id) is { IsPercent: true };
            string number = percent ? $"{Magnitude * 100f:0.#}%" : $"{Magnitude:0.#}";
            return $"{sign}{number} {StatNames.Label(Stat)}";
        }
    }

    /// <summary>The locale key of a regen effect's tooltip line ({0} is the signed number).</summary>
    public static string EffectKeyFor(AffixEffect effect) => effect switch
    {
        AffixEffect.HealthRegen => "affix.effect.health_regen",
        AffixEffect.StaminaRegen => "affix.effect.stamina_regen",
        AffixEffect.ManaRegen => "affix.effect.mana_regen",
        _ => "affix.effect.stat",
    };

    public Godot.Collections.Dictionary Save()
    {
        var data = new Godot.Collections.Dictionary
        {
            ["id"] = Id,
            ["label"] = Label,
            ["kind"] = (int)Kind,
            ["stat"] = (int)Stat,
            ["value"] = Magnitude,
            ["mod"] = (int)ModifierType,
        };

        if (Effect != AffixEffect.Stat)
        {
            data[EffectKey] = (int)Effect;
        }

        return data;
    }

    public static ItemAffix FromSave(Godot.Collections.Dictionary data)
    {
        AffixEffect effect = AffixEffect.Stat;
        if (data.TryGetValue(EffectKey, out Variant saved) &&
            System.Enum.IsDefined(typeof(AffixEffect), saved.AsInt32()))
        {
            effect = (AffixEffect)saved.AsInt32();
        }

        return new ItemAffix(
            data["id"].AsString(),
            data["label"].AsString(),
            (AffixKind)data["kind"].AsInt32(),
            (StatType)data["stat"].AsInt32(),
            data["value"].AsSingle(),
            (ModifierType)data["mod"].AsInt32(),
            effect);
    }
}
