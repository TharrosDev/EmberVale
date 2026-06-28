using System.Collections.Generic;
using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// Designer-authored tuning for a character's growth: the XP curve, the level cap,
/// the flat stat gains granted per level, and the skill points awarded per level.
/// A <see cref="ProgressionComponent"/> consumes one to drive levelling. New growth
/// profiles (player, an elite enemy archetype) are authored as <c>.tres</c> under
/// <c>data/progression/</c> with no code change.
/// </summary>
[GlobalClass]
public partial class ProgressionResource : Resource
{
    [Export] public int MaxLevel { get; set; } = 30;

    [ExportGroup("XP Curve")]
    /// <summary>XP required for the first level-up (1 → 2).</summary>
    [Export] public int BaseXpToLevel { get; set; } = 100;
    /// <summary>Growth exponent: xp(level) = round(BaseXpToLevel × level^exponent).</summary>
    [Export] public float XpCurveExponent { get; set; } = 1.5f;

    [ExportGroup("Per-Level Rewards")]
    [Export] public int SkillPointsPerLevel { get; set; } = 1;

    [ExportGroup("Per-Level Stat Gains (flat)")]
    [Export] public float HealthPerLevel { get; set; } = 8f;
    [Export] public float StaminaPerLevel { get; set; } = 4f;
    [Export] public float ManaPerLevel { get; set; }
    [Export] public float PhysicalPowerPerLevel { get; set; } = 1.5f;
    [Export] public float SpellPowerPerLevel { get; set; }
    [Export] public float ArmorPerLevel { get; set; } = 0.5f;

    /// <summary>XP needed to advance *from* <paramref name="level"/> to the next.
    /// Returns 0 at or beyond <see cref="MaxLevel"/>.</summary>
    public int XpToReach(int level)
    {
        return ProgressionMath.XpToReach(level, BaseXpToLevel, XpCurveExponent, MaxLevel);
    }

    /// <summary>The non-zero per-level stat gains as (stat, amount) pairs.</summary>
    public IEnumerable<(StatType Stat, float PerLevel)> StatGains()
    {
        if (HealthPerLevel != 0f) yield return (StatType.Health, HealthPerLevel);
        if (StaminaPerLevel != 0f) yield return (StatType.Stamina, StaminaPerLevel);
        if (ManaPerLevel != 0f) yield return (StatType.Mana, ManaPerLevel);
        if (PhysicalPowerPerLevel != 0f) yield return (StatType.PhysicalPower, PhysicalPowerPerLevel);
        if (SpellPowerPerLevel != 0f) yield return (StatType.SpellPower, SpellPowerPerLevel);
        if (ArmorPerLevel != 0f) yield return (StatType.Armor, ArmorPerLevel);
    }

    public static ProgressionResource CreateDefault() => new();
}
