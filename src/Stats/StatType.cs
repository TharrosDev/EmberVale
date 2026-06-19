namespace Embervale.Stats;

/// <summary>
/// The canonical set of character statistics. Three families:
///   * Resources  — have a current/max pair and deplete (Health, Stamina, Mana).
///   * Attributes — primary character-defining values raised by progression.
///   * Derived    — combat-facing values typically computed from attributes and gear.
///
/// Kept as an enum for type safety and fast dictionary keys. New entries can be
/// appended freely; designers tune *values* via <see cref="AttributeSet"/>
/// resources rather than code.
/// </summary>
public enum StatType
{
    // --- Resources (current/max) ---
    Health,
    Stamina,
    Mana,

    // --- Primary attributes ---
    Strength,
    Dexterity,
    Intelligence,
    Vitality,
    Endurance,

    // --- Derived / combat ---
    Armor,
    PhysicalPower,
    SpellPower,
    MoveSpeed,
    AttackSpeed,
    CritChance,
    CritDamage,
}

/// <summary>Helpers for classifying <see cref="StatType"/> values.</summary>
public static class StatTypes
{
    /// <summary>
    /// Resource stats track a depleting current value against their max.
    /// All other stats expose only a single computed value.
    /// </summary>
    public static bool IsResource(StatType type)
    {
        return type is StatType.Health or StatType.Stamina or StatType.Mana;
    }
}
