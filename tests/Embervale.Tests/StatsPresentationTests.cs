using System;
using System.Linq;
using Embervale.Combat;
using Embervale.Stats;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Phase 37.5C2: how the character screen reads a stat. Formatting is where a stat quietly lies —
/// Crit Chance is stored as 0..1 and reads as "0.08" without help, Crit Damage is a multiplier that
/// reads as "1.5 damage", and Armor is a raw number on a hyperbolic curve that means nothing at all
/// on its own.
/// </summary>
public class StatsPresentationTests
{
    // --- Formatting ---------------------------------------------------------

    [Fact]
    public void FractionStatsReadAsPercentages()
    {
        // 0.08 crit is 8%, not "0.08".
        Assert.Equal("8%", StatsPresentation.Format(StatType.CritChance, 0.08f));
    }

    [Fact]
    public void MultiplierStatsKeepTheirTimesSign()
    {
        Assert.Equal("×1.5", StatsPresentation.Format(StatType.CritDamage, 1.5f));
        Assert.Equal("×1", StatsPresentation.Format(StatType.AttackSpeed, 1f));
    }

    [Fact]
    public void WholeNumbersDoNotGrowADecimalTail()
    {
        Assert.Equal("8", StatsPresentation.Format(StatType.Armor, 8f));
        Assert.Equal("5.5", StatsPresentation.Format(StatType.MoveSpeed, 5.5f));
    }

    // --- Mitigation ---------------------------------------------------------

    /// <summary>
    /// The defence readout must be derived from the combat formula, never reimplemented, or the
    /// character screen and the damage pipeline drift and the screen is worse than showing nothing.
    /// </summary>
    [Fact]
    public void MitigationMatchesTheCombatCurveExactly()
    {
        foreach (float armor in new[] { 0f, 8f, 25f, 100f, 400f })
        {
            Assert.Equal(1f - CombatMath.ArmorMultiplier(armor), StatsPresentation.MitigationFraction(armor), 5);
        }
    }

    [Fact]
    public void ZeroMitigationIsZeroAndOneHundredArmourIsHalf()
    {
        Assert.Equal(0f, StatsPresentation.MitigationFraction(0f), 5);
        Assert.Equal(0.5f, StatsPresentation.MitigationFraction(100f), 5);
    }

    /// <summary>
    /// Resistance is never immunity (DESIGN's "no school a trap"), so the displayed reduction stays
    /// strictly below 100% — a screen showing "100% reduced" promises something combat will not
    /// honour.
    ///
    /// ⚠️ **This holds on the reachable domain, not on all floats.** `100 / (100 + x)` underflows
    /// to exactly 0 somewhere above x ≈ 1e9, so at absurd inputs the curve really does return
    /// immunity — and it does so in <see cref="CombatMath.ArmorMultiplier"/> itself, meaning combat
    /// would grant it too. That is a property of the shared formula, not of this display layer, and
    /// clamping it here would only make the character screen disagree with the damage pipeline,
    /// which is the one thing this readout must never do. The bound tested is six orders of
    /// magnitude above anything gear can roll; if a stat ever approaches it, fix the curve.
    /// </summary>
    [Fact]
    public void MitigationNeverReachesImmunityInAnyReachableRange()
    {
        foreach (float armor in new[] { 1_000f, 100_000f, 1_000_000f })
        {
            float fraction = StatsPresentation.MitigationFraction(armor);
            Assert.True(fraction < 1f, $"armor {armor} displayed as {fraction:P} — that reads as immunity");
        }
    }

    /// <summary>A negative value cannot display as a *bonus* to incoming damage: the combat curve
    /// clamps negatives to zero mitigation rather than amplifying, and the screen has to agree.</summary>
    [Fact]
    public void NegativeMitigationClampsRatherThanAmplifying()
    {
        Assert.Equal(0f, StatsPresentation.MitigationFraction(-50f), 5);
    }

    // --- Coverage -----------------------------------------------------------

    /// <summary>
    /// Every stat the player can hold is either displayed or is a resource the HUD owns. This is
    /// the guard against the thing 37.5C2 exists to fix: a stat that exists on the player and is
    /// shown nowhere. Adding a `StatType` without deciding where it appears fails here.
    /// </summary>
    [Fact]
    public void EveryNonResourceStatIsDisplayedSomewhere()
    {
        var displayed = StatsPresentation.Displayed().ToHashSet();

        foreach (StatType stat in Enum.GetValues<StatType>())
        {
            if (StatTypes.IsResource(stat))
            {
                continue; // Health/Stamina/Mana are the HUD's vitals, not a character-sheet row.
            }

            Assert.True(displayed.Contains(stat), $"{stat} is on the player but appears on no screen");
        }
    }

    [Fact]
    public void NoStatIsListedTwice()
    {
        var all = StatsPresentation.Displayed().ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    /// <summary>Armor and all six school resistances take the mitigation treatment; nothing else
    /// does. A power stat showing "% reduced" would be nonsense.</summary>
    [Fact]
    public void OnlyMitigationStatsCarryTheReductionNote()
    {
        Assert.True(StatsPresentation.IsMitigation(StatType.Armor));
        Assert.True(StatsPresentation.IsMitigation(StatType.NecroticResist));
        Assert.False(StatsPresentation.IsMitigation(StatType.PhysicalPower));
        Assert.False(StatsPresentation.IsMitigation(StatType.CritChance));

        int mitigators = Enum.GetValues<StatType>().Count(StatsPresentation.IsMitigation);
        Assert.Equal(7, mitigators); // Armor + six schools
    }
}
