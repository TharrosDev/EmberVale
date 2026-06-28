using Embervale.Localization;

namespace Embervale.Stats;

/// <summary>
/// Localized display names for <see cref="StatType"/> (Phase 26D). Stat names are UI vocabulary —
/// the character creator's trait summary, future tooltips — so they live behind <see cref="Loc"/>
/// keys (CLAUDE.md §6) rather than the enum's identifier.
/// </summary>
public static class StatNames
{
    /// <summary>The player-facing name for a stat, e.g. <c>StatType.SpellPower</c> → "Spell Power".</summary>
    public static string Label(StatType stat) => Loc.T(Key(stat));

    /// <summary>The locale key for a stat (pure — no Godot), exposed for the round-trip unit test.</summary>
    public static string Key(StatType stat) => stat switch
    {
        StatType.Health => "stat.health",
        StatType.Stamina => "stat.stamina",
        StatType.Mana => "stat.mana",
        StatType.Strength => "stat.strength",
        StatType.Dexterity => "stat.dexterity",
        StatType.Intelligence => "stat.intelligence",
        StatType.Vitality => "stat.vitality",
        StatType.Endurance => "stat.endurance",
        StatType.Armor => "stat.armor",
        StatType.PhysicalPower => "stat.physical_power",
        StatType.SpellPower => "stat.spell_power",
        StatType.MoveSpeed => "stat.move_speed",
        StatType.AttackSpeed => "stat.attack_speed",
        StatType.CritChance => "stat.crit_chance",
        StatType.CritDamage => "stat.crit_damage",
        _ => "stat.unknown",
    };
}
