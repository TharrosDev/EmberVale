using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>One line of "what a point of this primary buys". A stat effect names a <see cref="StatType"/>;
    /// a non-stat effect (dodge, mana cost) names a Loc key instead, so the panel resolves the label.</summary>
    public readonly record struct PerPointPart(string? NameKey, StatType? Stat, string Amount);

    /// <summary>The per-point effects of a primary, read from <see cref="StatDerivation"/> so the panel can
    /// never disagree with what the stats actually grant. Empty for a non-primary.</summary>
    public static IReadOnlyList<PerPointPart> PerPoint(StatType primary)
    {
        var parts = new List<PerPointPart>();
        foreach (StatDerivation.Effect effect in StatDerivation.Effects(primary))
        {
            parts.Add(new PerPointPart(null, effect.Stat, FormatPerPoint(effect.Stat, effect.PerPoint)));
        }

        if (primary == StatType.Dexterity)
        {
            parts.Add(new PerPointPart("char.pp_dodge", null, Signed(StatDerivation.DodgePerPoint * 100f) + "%"));
        }
        else if (primary == StatType.Intelligence)
        {
            parts.Add(new PerPointPart("char.pp_mana_cost", null, Signed(StatDerivation.ManaCostPerPoint * 100f) + "%"));
        }

        return parts;
    }

    /// <summary>A per-point bonus as the player reads it: fractions and multipliers as a signed percentage
    /// ("+0.2%"), everything else as a signed number ("+0.8").</summary>
    public static string FormatPerPoint(StatType stat, float perPoint) =>
        IsFraction(stat) || IsMultiplier(stat) ? Signed(perPoint * 100f) + "%" : Signed(perPoint);

    private static string Signed(float value) =>
        (value > 0f ? "+" : string.Empty) + value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A change to a stat as the player reads it, always signed: "+6", "-2.5", and a percentage for a
    /// fraction or a multiplier ("+2%" Crit Chance, "+5%" Attack Speed), which are stored against 1.</summary>
    public static string FormatDelta(StatType stat, float delta) =>
        IsFraction(stat) || IsMultiplier(stat) ? Signed(delta * 100f) + "%" : Signed(delta);

    /// <summary>
    /// What a change of gear does to the whole sheet. Takes the raw stat differences between two items
    /// (what <see cref="ItemPresentation"/>'s comparison returns) and adds what the
    /// primaries among them buy through <see cref="StatDerivation"/>: +2 Strength is also +1.6 Physical
    /// Power, and a comparison that stops at "+2 Strength" hides the half the player cares about.
    ///
    /// Ordered as the sheet is (<see cref="Displayed"/>), then anything the sheet does not list, such as
    /// the three resources, in stat order. Stats that come out unchanged are dropped.
    /// </summary>
    public static IReadOnlyList<(StatType Stat, float Delta)> DerivedDelta(IEnumerable<(StatType Stat, float Delta)> itemDeltas)
    {
        var totals = new Dictionary<StatType, float>();
        foreach ((StatType stat, float delta) in itemDeltas)
        {
            totals[stat] = totals.GetValueOrDefault(stat) + delta;
            foreach (StatDerivation.Effect bonus in StatDerivation.Bonuses(stat, delta))
            {
                totals[bonus.Stat] = totals.GetValueOrDefault(bonus.Stat) + bonus.PerPoint;
            }
        }

        var result = new List<(StatType, float)>();
        var listed = new HashSet<StatType>();
        foreach (StatType stat in Displayed())
        {
            listed.Add(stat);
            if (totals.TryGetValue(stat, out float delta) && System.Math.Abs(delta) > 0.0001f)
            {
                result.Add((stat, delta));
            }
        }

        var rest = new List<StatType>(totals.Keys);
        rest.Sort();
        foreach (StatType stat in rest)
        {
            if (!listed.Contains(stat) && System.Math.Abs(totals[stat]) > 0.0001f)
            {
                result.Add((stat, totals[stat]));
            }
        }

        return result;
    }

    /// <summary>
    /// The stats that may lead each of <see cref="Sections"/>, in the same order: any primary for the
    /// attributes, physical or spell power for offence, armour for defence.
    /// </summary>
    private static readonly StatType[][] HeroCandidates =
    {
        new[] { StatType.Strength, StatType.Dexterity, StatType.Intelligence, StatType.Vitality, StatType.Endurance },
        new[] { StatType.PhysicalPower, StatType.SpellPower },
        new[] { StatType.Armor },
    };

    /// <summary>
    /// The one stat a section leads with: the highest of its candidates, the first listed on a tie. So
    /// a mage's offence leads with Spell Power and a fighter's with Physical Power, without the sheet
    /// asking what the character is. Null for a section index outside <see cref="Sections"/>.
    /// </summary>
    public static StatType? SectionHero(int section, System.Func<StatType, float> valueOf)
    {
        if (section < 0 || section >= HeroCandidates.Length)
        {
            return null;
        }

        StatType best = HeroCandidates[section][0];
        float bestValue = valueOf(best);
        foreach (StatType stat in HeroCandidates[section])
        {
            float value = valueOf(stat);
            if (value > bestValue)
            {
                best = stat;
                bestValue = value;
            }
        }

        return best;
    }

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
