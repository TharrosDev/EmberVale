using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the XP curve and the level-up resolution — the boundary territory behind
/// <see cref="ProgressionComponent.AddXp"/>. The component's event/skill-point side-effects run
/// in-engine; the arithmetic that decides *how many* levels a grant covers lives in
/// <see cref="ProgressionMath"/> and is exercised here.
/// </summary>
public class ProgressionMathTests
{
    private const int BaseXp = 100;
    private const float Exponent = 1.5f;
    private const int MaxLevel = 50; // pins data/progression/PlayerProgression.tres

    private static int Curve(int level) => ProgressionMath.XpToReach(level, BaseXp, Exponent, MaxLevel);

    // --- XpToReach (the curve) ---------------------------------------------

    [Fact]
    public void XpToReach_IsPositiveAndStrictlyIncreasingBelowCap()
    {
        int previous = 0;
        for (int level = 1; level < MaxLevel; level++)
        {
            int need = Curve(level);
            Assert.True(need > 0, $"level {level} should need positive xp");
            Assert.True(need > previous, $"cost should rise with level (level {level})");
            previous = need;
        }
    }

    [Theory]
    [InlineData(50)] // exactly the cap
    [InlineData(51)] // beyond the cap
    public void XpToReach_IsZeroAtOrBeyondCap(int level)
    {
        Assert.Equal(0, Curve(level));
    }

    [Fact]
    public void XpToReach_FirstLevelMatchesBase()
    {
        // round(100 * 1^1.5) = 100.
        Assert.Equal(100, Curve(1));
    }

    // --- Resolve (level-up boundaries) -------------------------------------

    [Fact]
    public void Resolve_OneShortOfThreshold_DoesNotLevel()
    {
        int need = Curve(1);
        (int level, int xp, int gained) = ProgressionMath.Resolve(1, 0, MaxLevel, need - 1, Curve);
        Assert.Equal(1, level);
        Assert.Equal(need - 1, xp);
        Assert.Equal(0, gained);
    }

    [Fact]
    public void Resolve_ExactThreshold_LevelsWithZeroRemainder()
    {
        int need = Curve(1);
        (int level, int xp, int gained) = ProgressionMath.Resolve(1, 0, MaxLevel, need, Curve);
        Assert.Equal(2, level);
        Assert.Equal(0, xp);
        Assert.Equal(1, gained);
    }

    [Fact]
    public void Resolve_LargeGrant_SpansMultipleLevels()
    {
        int grant = Curve(1) + Curve(2) + Curve(3) + 5; // enough for three level-ups plus change
        (int level, int xp, int gained) = ProgressionMath.Resolve(1, 0, MaxLevel, grant, Curve);
        Assert.Equal(4, level);
        Assert.Equal(3, gained);
        Assert.Equal(5, xp);
    }

    [Fact]
    public void Resolve_OverflowAtCap_DiscardsExcessXp()
    {
        // A grant far larger than the whole curve must stop exactly at the cap with no leftover xp.
        (int level, int xp, int gained) = ProgressionMath.Resolve(1, 0, MaxLevel, 100_000_000, Curve);
        Assert.Equal(MaxLevel, level);
        Assert.Equal(0, xp);
        Assert.Equal(MaxLevel - 1, gained);
    }

    [Fact]
    public void Resolve_AlreadyAtCap_IsNoOpAndPinsXpToZero()
    {
        (int level, int xp, int gained) = ProgressionMath.Resolve(MaxLevel, 999, MaxLevel, 500, Curve);
        Assert.Equal(MaxLevel, level);
        Assert.Equal(0, xp);
        Assert.Equal(0, gained);
    }

    [Fact]
    public void Resolve_NonPositiveGrant_IsNoOp()
    {
        (int level, int xp, int gained) = ProgressionMath.Resolve(3, 40, MaxLevel, 0, Curve);
        Assert.Equal(3, level);
        Assert.Equal(40, xp);
        Assert.Equal(0, gained);
    }

    [Fact]
    public void Resolve_NonPositiveCost_BreaksInsteadOfLoopingForever()
    {
        // A degenerate curve that always returns 0 must not spin: the loop bails on need<=0.
        (int level, int xp, int gained) = ProgressionMath.Resolve(1, 0, MaxLevel, 1000, _ => 0);
        Assert.Equal(1, level);
        Assert.Equal(1000, xp);
        Assert.Equal(0, gained);
    }

    // --- Milestones (bonus skill points) -----------------------------------

    private static readonly int[] Milestones = { 10, 20, 30, 40, 50 };

    [Theory]
    [InlineData(0, 9, 0)]    // below the first milestone
    [InlineData(0, 10, 1)]   // exactly on it
    [InlineData(9, 10, 1)]   // crossing it by one level
    [InlineData(10, 10, 0)]  // already paid
    [InlineData(0, 50, 5)]   // a fresh character jumping to the cap is paid every milestone
    [InlineData(19, 41, 3)]  // 20, 30 and 40 only
    public void MilestonesCrossed_CountsOnlyUnpaidMilestonesInRange(int claimedUpTo, int level, int expected)
    {
        Assert.Equal(expected, ProgressionMath.MilestonesCrossed(claimedUpTo, level, Milestones));
    }

    [Fact]
    public void MilestonesCrossed_WithNoMilestones_IsZero()
    {
        Assert.Equal(0, ProgressionMath.MilestonesCrossed(0, 50, null));
        Assert.Equal(0, ProgressionMath.MilestonesCrossed(0, 50, System.Array.Empty<int>()));
    }

    [Fact]
    public void ShippedCurve_EarnsThePerkCatalogueSupply()
    {
        // 49 level points + 5 milestone points = 54, the figure the perk trees are sized for.
        Assert.Equal(PerkCatalogue.SkillPointSupply, ((MaxLevel - 1) * 1) + ProgressionMath.MilestonesCrossed(0, MaxLevel, Milestones));
    }
}
