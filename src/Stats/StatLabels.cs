namespace Embervale.Stats;

/// <summary>Short, human-readable display names for <see cref="StatType"/> values
/// (used by item tooltips and any stat read-out). Kept beside the enum so new
/// stats get a label in one place.</summary>
public static class StatLabels
{
    public static string Short(StatType type)
    {
        return type switch
        {
            StatType.Health => "Max Health",
            StatType.Stamina => "Max Stamina",
            StatType.Mana => "Max Mana",
            StatType.PhysicalPower => "Physical Power",
            StatType.SpellPower => "Spell Power",
            StatType.MoveSpeed => "Move Speed",
            StatType.AttackSpeed => "Attack Speed",
            StatType.CritChance => "Crit Chance",
            StatType.CritDamage => "Crit Damage",
            _ => type.ToString(),
        };
    }
}
