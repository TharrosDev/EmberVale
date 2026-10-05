using System;
using System.Collections.Generic;

namespace Embervale.Stats;

/// <summary>
/// What each primary attribute buys, per point INVESTED (final value minus base value). Pure and
/// Godot-free: <see cref="StatDerivationComponent"/> applies it, the stats panel reads the same
/// table for its per-point text, and the tests pin it.
///
/// Measuring points off the base keeps every actor's authored numbers unchanged: an actor sitting at
/// its base primaries gets +0. Only race deltas, gear, perks and level growth move the derived stats.
/// </summary>
public static class StatDerivation
{
    /// <summary>One flat bonus: <paramref name="PerPoint"/> added to <paramref name="Stat"/> per point.</summary>
    public readonly record struct Effect(StatType Stat, float PerPoint);

    /// <summary>Dodge stamina cost change per Dexterity point (-0.5%), floored at <see cref="DodgeFloor"/>.</summary>
    public const float DodgePerPoint = -0.005f;

    /// <summary>The lowest dodge stamina multiplier Dexterity can reach (-25%).</summary>
    public const float DodgeFloor = 0.75f;

    /// <summary>Spell mana cost change per Intelligence point (-0.4%), floored at <see cref="ManaCostFloor"/>.</summary>
    public const float ManaCostPerPoint = -0.004f;

    /// <summary>The lowest spell mana multiplier Intelligence can reach (-20%).</summary>
    public const float ManaCostFloor = 0.8f;

    /// <summary>The primaries, in display order.</summary>
    public static readonly StatType[] Primaries =
    {
        StatType.Strength, StatType.Dexterity, StatType.Intelligence, StatType.Vitality, StatType.Endurance,
    };

    private static readonly Effect[] None = Array.Empty<Effect>();

    private static readonly Effect[] Str = { new(StatType.PhysicalPower, 0.8f) };
    private static readonly Effect[] Dex = { new(StatType.CritChance, 0.002f), new(StatType.AttackSpeed, 0.003f) };
    private static readonly Effect[] Int = { new(StatType.SpellPower, 0.7f), new(StatType.Mana, 4f) };
    private static readonly Effect[] Vit = { new(StatType.Health, 5f), new(StatType.Armor, 0.3f) };
    private static readonly Effect[] End = { new(StatType.Stamina, 3f) };

    public static bool IsPrimary(StatType stat) =>
        stat is StatType.Strength or StatType.Dexterity or StatType.Intelligence
            or StatType.Vitality or StatType.Endurance;

    /// <summary>The flat per-point effects of a primary; empty for any other stat.</summary>
    public static IReadOnlyList<Effect> Effects(StatType primary) => primary switch
    {
        StatType.Strength => Str,
        StatType.Dexterity => Dex,
        StatType.Intelligence => Int,
        StatType.Vitality => Vit,
        StatType.Endurance => End,
        _ => None,
    };

    /// <summary>The bonuses <paramref name="points"/> invested points of <paramref name="primary"/> grant.</summary>
    public static IEnumerable<Effect> Bonuses(StatType primary, float points)
    {
        foreach (Effect effect in Effects(primary))
        {
            yield return new Effect(effect.Stat, effect.PerPoint * points);
        }
    }

    /// <summary>Multiplier on dodge stamina cost from Dexterity invested above base. 1 at base, never below
    /// <see cref="DodgeFloor"/>. Not hooked into the dodge yet.</summary>
    public static float DodgeStaminaFactor(float dexDelta) =>
        Math.Max(DodgeFloor, 1f + (dexDelta * DodgePerPoint));

    /// <summary>Multiplier on spell mana cost from Intelligence invested above base. 1 at base, never below
    /// <see cref="ManaCostFloor"/>. Not hooked into spellcasting yet.</summary>
    public static float ManaCostFactor(float intDelta) =>
        Math.Max(ManaCostFloor, 1f + (intDelta * ManaCostPerPoint));

    /// <summary>
    /// What levelling alone adds by <paramref name="level"/>: each stat's own per-level gain plus the
    /// derived bonuses its primaries' growth buys. Level 1 grants nothing, matching
    /// <c>ProgressionComponent.ApplyGrowth</c>. Used to pin the player's level-50 totals.
    /// </summary>
    public static Dictionary<StatType, float> GrowthTotals(IEnumerable<(StatType Stat, float PerLevel)> gains, int level)
    {
        var totals = new Dictionary<StatType, float>();
        float levels = Math.Max(0, level - 1);
        foreach ((StatType stat, float perLevel) in gains)
        {
            totals[stat] = totals.GetValueOrDefault(stat) + (perLevel * levels);
            foreach (Effect bonus in Bonuses(stat, perLevel * levels))
            {
                totals[bonus.Stat] = totals.GetValueOrDefault(bonus.Stat) + bonus.PerPoint;
            }
        }

        return totals;
    }
}
