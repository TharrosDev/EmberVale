using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Stats;

namespace Embervale.UI;

/// <summary>
/// How a stat reads on the character screen (Phase 37.5C2).
///
/// Until this phase **the game never showed the player a single stat.** `InventoryPanel` had no
/// `StatsComponent` reference at all, so Armor, Physical Power, Spell Power, Crit Chance, Move
/// Speed and the six Phase 34E resistances existed on the player and were displayed nowhere. That
/// also left 37.5C's comparison half-blind: it could say a sword was +6 Armor while the player had
/// no way to learn what their Armor was.
///
/// Pure and Godot-free so it can be tested — the formatting is where a stat quietly lies. Crit
/// Chance is stored as 0..1 and would read as "0.08" without help; Armor is a raw number on a
/// hyperbolic curve and means nothing at all on its own.
/// </summary>
public static class StatsPresentation
{
    /// <summary>The character screen's stat groups, in display order. Resources are omitted: they
    /// are the HUD's job and change second to second, which is the opposite of what this screen is
    /// for.</summary>
    public static readonly (string HeaderKey, StatType[] Stats)[] Sections =
    {
        ("char.stats_attributes", new[]
        {
            StatType.Strength, StatType.Dexterity, StatType.Intelligence,
            StatType.Vitality, StatType.Endurance,
        }),
        ("char.stats_combat", new[]
        {
            StatType.PhysicalPower, StatType.SpellPower, StatType.CritChance,
            StatType.CritDamage, StatType.AttackSpeed, StatType.MoveSpeed,
        }),
        ("char.stats_defence", new[]
        {
            StatType.Armor, StatType.FireResist, StatType.FrostResist, StatType.LightningResist,
            StatType.ArcaneResist, StatType.NatureResist, StatType.NecroticResist,
        }),
    };

    /// <summary>Stats stored as a 0..1 fraction. Shown as a percentage, because "0.08 Crit Chance"
    /// is a number the player has to decode rather than read.</summary>
    public static bool IsFraction(StatType stat) => stat is StatType.CritChance;

    /// <summary>Stats stored as a multiplier against a baseline of 1. Shown with a × so a value of
    /// 1.5 does not read as "1.5 damage".</summary>
    public static bool IsMultiplier(StatType stat) => stat is StatType.CritDamage or StatType.AttackSpeed;

    /// <summary>
    /// Stats that mitigate damage on <see cref="CombatMath.ArmorMultiplier"/>'s curve — Armor and
    /// the six per-school resistances. These get the derived percentage alongside the raw number.
    /// </summary>
    public static bool IsMitigation(StatType stat) => stat is StatType.Armor
        or StatType.FireResist or StatType.FrostResist or StatType.LightningResist
        or StatType.ArcaneResist or StatType.NatureResist or StatType.NecroticResist;

    /// <summary>
    /// The share of incoming damage a mitigation value actually removes, 0..1.
    ///
    /// Derived from <see cref="CombatMath.ArmorMultiplier"/> rather than reimplemented, so the
    /// number on the character screen can never disagree with the number combat uses. This is the
    /// whole reason the defence section is worth showing: "Armor 8" is opaque, and the curve is
    /// hyperbolic, so a player cannot infer that it is about 7% and that doubling it is not
    /// double the benefit.
    /// </summary>
    public static float MitigationFraction(float value) => 1f - CombatMath.ArmorMultiplier(value);

    /// <summary>The stat's value as the player should read it.</summary>
    public static string Format(StatType stat, float value)
    {
        if (IsFraction(stat))
        {
            return $"{value * 100f:0.#}%";
        }

        if (IsMultiplier(stat))
        {
            return $"×{value:0.##}";
        }

        return value == (int)value ? ((int)value).ToString() : value.ToString("0.##");
    }

    // No string-building for the mitigation note lives here on purpose. Every player-facing string
    // goes through Loc (UI_STYLE §7), Loc reads the catalogue through Godot, and pulling it in
    // would cost this class the thing it exists for. The panel formats MitigationFraction with
    // `char.stat_reduced`; a zero value still renders that line rather than being omitted, because
    // an absent line reads as "not applicable" and a resistance of zero is very much applicable.

    /// <summary>Every stat the sections display, for tests and for validation.</summary>
    public static IEnumerable<StatType> Displayed()
    {
        foreach ((string _, StatType[] stats) in Sections)
        {
            foreach (StatType stat in stats)
            {
                yield return stat;
            }
        }
    }
}
