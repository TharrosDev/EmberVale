using System.Collections.Generic;
using System.Linq;
using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The primaries' coefficient table and the player's level-50 totals. The component that applies it
/// is a Godot node and runs in-engine (--lifecycle); everything that decides the numbers is here.
/// </summary>
public class StatDerivationTests
{
    // Mirrors data/progression/PlayerProgression.tres (ContentValidator.ValidatePlayerGrowth checks the real file).
    private static readonly (StatType, float)[] PlayerGains =
    {
        (StatType.Health, 6.75f), (StatType.Stamina, 3.25f), (StatType.Mana, 1f),
        (StatType.PhysicalPower, 1.3f), (StatType.SpellPower, 0.175f), (StatType.Armor, 0.425f),
        (StatType.Strength, 0.25f), (StatType.Dexterity, 0.25f), (StatType.Intelligence, 0.25f),
        (StatType.Vitality, 0.25f), (StatType.Endurance, 0.25f),
    };

    private static float Bonus(StatType primary, StatType target, float points) =>
        StatDerivation.Bonuses(primary, points).Where(b => b.Stat == target).Sum(b => b.PerPoint);

    [Fact]
    public void CoefficientTableIsPinned()
    {
        Assert.Equal(0.8f, Bonus(StatType.Strength, StatType.PhysicalPower, 1f), 5);
        Assert.Equal(0.002f, Bonus(StatType.Dexterity, StatType.CritChance, 1f), 5);
        Assert.Equal(0.003f, Bonus(StatType.Dexterity, StatType.AttackSpeed, 1f), 5);
        Assert.Equal(0.7f, Bonus(StatType.Intelligence, StatType.SpellPower, 1f), 5);
        Assert.Equal(4f, Bonus(StatType.Intelligence, StatType.Mana, 1f), 5);
        Assert.Equal(5f, Bonus(StatType.Vitality, StatType.Health, 1f), 5);
        Assert.Equal(0.3f, Bonus(StatType.Vitality, StatType.Armor, 1f), 5);
        Assert.Equal(3f, Bonus(StatType.Endurance, StatType.Stamina, 1f), 5);
    }

    [Fact]
    public void EveryPrimaryBuysSomethingAndNothingElseDoes()
    {
        foreach (StatType primary in StatDerivation.Primaries)
        {
            Assert.True(StatDerivation.IsPrimary(primary));
            Assert.NotEmpty(StatDerivation.Effects(primary));
        }

        foreach (StatType other in System.Enum.GetValues<StatType>().Except(StatDerivation.Primaries))
        {
            Assert.False(StatDerivation.IsPrimary(other));
            Assert.Empty(StatDerivation.Effects(other));
        }
    }

    [Fact]
    public void DerivedStatsNeverFeedBackIntoPrimaries()
    {
        // The component writes derived stats from primaries; a primary among the targets would loop.
        foreach (StatType primary in StatDerivation.Primaries)
        {
            Assert.DoesNotContain(StatDerivation.Effects(primary), e => StatDerivation.IsPrimary(e.Stat));
        }
    }

    [Fact]
    public void ZeroPointsGrantNothing()
    {
        foreach (StatType primary in StatDerivation.Primaries)
        {
            Assert.All(StatDerivation.Bonuses(primary, 0f), b => Assert.Equal(0f, b.PerPoint));
        }
    }

    [Fact]
    public void BonusesScaleLinearlyAndNegativePointsCost()
    {
        Assert.Equal(10f, Bonus(StatType.Vitality, StatType.Health, 2f), 5);
        Assert.Equal(-5f, Bonus(StatType.Vitality, StatType.Health, -1f), 5);
    }

    [Fact]
    public void DodgeStaminaFactorFallsHalfAPercentPerPointAndFloorsAtThreeQuarters()
    {
        Assert.Equal(1f, StatDerivation.DodgeStaminaFactor(0f), 5);
        Assert.Equal(0.95f, StatDerivation.DodgeStaminaFactor(10f), 5);
        Assert.Equal(0.75f, StatDerivation.DodgeStaminaFactor(50f), 5);
        Assert.Equal(0.75f, StatDerivation.DodgeStaminaFactor(5000f), 5);
    }

    [Fact]
    public void ManaCostFactorFallsFourTenthsOfAPercentPerPointAndFloorsAtEightyPercent()
    {
        Assert.Equal(1f, StatDerivation.ManaCostFactor(0f), 5);
        Assert.Equal(0.96f, StatDerivation.ManaCostFactor(10f), 5);
        Assert.Equal(0.8f, StatDerivation.ManaCostFactor(50f), 5);
        Assert.Equal(0.8f, StatDerivation.ManaCostFactor(5000f), 5);
    }

    // --- the level-50 pin ----------------------------------------------------

    [Fact]
    public void LevelOneGrantsNothing()
    {
        Assert.All(StatDerivation.GrowthTotals(PlayerGains, 1).Values, v => Assert.Equal(0f, v));
    }

    /// <summary>
    /// The retune moved growth from per-level stats into the primaries without changing how strong a
    /// level-50 character is: Health 8/level, Stamina 4, Physical Power 1.5 and Armor 0.5 all land on
    /// their old totals once the primaries' derived bonuses are counted. Casters gain what they lacked.
    /// </summary>
    [Fact]
    public void PlayerLevelFiftyTotalsAreUnchangedForTheRebalancedStats()
    {
        Dictionary<StatType, float> totals = StatDerivation.GrowthTotals(PlayerGains, 50);

        Assert.Equal(8f * 49f, totals[StatType.Health], 2);
        Assert.Equal(4f * 49f, totals[StatType.Stamina], 2);
        Assert.Equal(1.5f * 49f, totals[StatType.PhysicalPower], 2);
        Assert.Equal(0.5f * 49f, totals[StatType.Armor], 2);
    }

    [Fact]
    public void PlayerLevelFiftyGrowsEveryPrimaryAndGivesCastersSpellPowerAndMana()
    {
        Dictionary<StatType, float> totals = StatDerivation.GrowthTotals(PlayerGains, 50);

        foreach (StatType primary in StatDerivation.Primaries)
        {
            Assert.Equal(0.25f * 49f, totals[primary], 3);
        }

        Assert.Equal(17.15f, totals[StatType.SpellPower], 2);
        Assert.Equal(98f, totals[StatType.Mana], 2);
        Assert.Equal(0.002f * 12.25f, totals[StatType.CritChance], 4);
        Assert.Equal(0.003f * 12.25f, totals[StatType.AttackSpeed], 4);
    }
}
