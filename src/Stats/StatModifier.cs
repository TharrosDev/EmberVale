namespace Embervale.Stats;

/// <summary>How a <see cref="StatModifier"/> combines into a stat's final value.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ModifierType
{
    /// <summary>Added to the base before any percentages. e.g. +10 Strength.</summary>
    Flat,

    /// <summary>Summed with other PercentAdd, then applied as one multiplier. e.g. +15%.</summary>
    PercentAdd,

    /// <summary>Applied as its own independent multiplier (stacks multiplicatively).</summary>
    PercentMult,
}

/// <summary>
/// A single additive or multiplicative change to a <see cref="Stat"/>. Modifiers
/// are the universal currency for equipment bonuses, buffs, debuffs, auras and
/// passive perks. Each carries an optional <see cref="Source"/> so all bonuses
/// from one origin (a removed item, an expired buff) can be stripped at once.
/// </summary>
public sealed class StatModifier
{
    public StatModifier(float value, ModifierType type, object? source = null)
    {
        Value = value;
        Type = type;
        Source = source;
    }

    public float Value { get; }

    public ModifierType Type { get; }

    /// <summary>Origin object (item, buff, perk). Used for bulk removal.</summary>
    public object? Source { get; }
}
