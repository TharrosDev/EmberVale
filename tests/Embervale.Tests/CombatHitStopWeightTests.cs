using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>Covers the weight of a freeze frame: outcome and kind set it, the comfort scale shrinks it
/// (and softens its depth), the budget stops a flurry stacking, and nothing exceeds the hard cap.</summary>
public class CombatHitStopWeightTests
{
    private static int Ms(HitOutcome outcome, HitKind kind = HitKind.Normal, float amount = 20f, bool staggered = false) =>
        HitStop.Plan(outcome, kind, amount, staggered).Milliseconds;

    [Fact]
    public void Outcomes_AreOrderedByWeight()
    {
        Assert.True(Ms(HitOutcome.GuardBroken) > Ms(HitOutcome.PoiseBroken));
        Assert.True(Ms(HitOutcome.PoiseBroken) > Ms(HitOutcome.Parried));
        Assert.True(Ms(HitOutcome.Parried) > Ms(HitOutcome.Critical, amount: 12f));
        Assert.True(Ms(HitOutcome.Hit) > Ms(HitOutcome.Blocked));
    }

    [Fact]
    public void HeavyKinds_FreezeLongerThanLightAndRangedLess()
    {
        Assert.True(Ms(HitOutcome.Hit, HitKind.Heavy) > Ms(HitOutcome.Hit));
        Assert.True(Ms(HitOutcome.Hit, HitKind.Charged) > Ms(HitOutcome.Hit, HitKind.Heavy));
        Assert.True(Ms(HitOutcome.Hit, HitKind.Ranged) < Ms(HitOutcome.Hit));
        Assert.True(Ms(HitOutcome.Hit, HitKind.Spell) < Ms(HitOutcome.Hit, HitKind.Ranged));
    }

    [Fact]
    public void ARiposteOutweighsACrit() =>
        Assert.True(Ms(HitOutcome.Critical, HitKind.Riposte) > Ms(HitOutcome.Critical));

    [Fact]
    public void TrivialChip_StillDoesNotFreeze() =>
        Assert.True(HitStop.Plan(HitOutcome.Hit, HitKind.Normal, HitStop.MinDamage - 1f, false).IsNone);

    [Fact]
    public void ABlowThatAlsoBrokePoise_HoldsForTheBreak()
    {
        Assert.True(Ms(HitOutcome.Hit, staggered: true) >= HitStop.StaggerMs);
        Assert.True(Ms(HitOutcome.Critical, staggered: true) >= HitStop.StaggerMs);
    }

    [Fact]
    public void NothingFreezesPastTheCap()
    {
        foreach (HitOutcome outcome in System.Enum.GetValues<HitOutcome>())
        {
            foreach (HitKind kind in System.Enum.GetValues<HitKind>())
            {
                Assert.InRange(HitStop.Plan(outcome, kind, 500f, true, 5f).Milliseconds, 0, HitStop.MaxMs);
            }
        }
    }

    [Fact]
    public void AuthoredActionScale_MultipliesAndZeroSilences()
    {
        int normal = HitStop.Plan(HitOutcome.Hit, HitKind.Normal, 20f, false, 1f).Milliseconds;
        Assert.True(HitStop.Plan(HitOutcome.Hit, HitKind.Normal, 20f, false, 2f).Milliseconds > normal);
        Assert.True(HitStop.Plan(HitOutcome.Hit, HitKind.Normal, 20f, false, 0f).IsNone);
    }

    [Fact]
    public void ShortStops_AreADragNotAHardFreeze()
    {
        HitStopPlan light = HitStop.Plan(HitOutcome.Hit, HitKind.Normal, HitStop.MinDamage, false);
        Assert.True(light.Milliseconds < HitStop.HardFreezeMs);
        Assert.True(light.TimeScale > 0f);
        HitStopPlan heavy = HitStop.Plan(HitOutcome.GuardBroken, HitKind.Normal, 30f, false);
        Assert.Equal(0f, heavy.TimeScale);
    }

    [Fact]
    public void Scale_ZeroIsNoStopAndOneIsUnchanged()
    {
        HitStopPlan plan = HitStop.Plan(HitOutcome.GuardBroken, HitKind.Normal, 30f, false);
        Assert.True(HitStop.Scale(plan, 0f).IsNone);
        Assert.Equal(plan, HitStop.Scale(plan, 1f));
    }

    [Fact]
    public void Scale_AtReducedMotionShortensAndSoftensTheDip()
    {
        HitStopPlan plan = HitStop.Plan(HitOutcome.GuardBroken, HitKind.Normal, 30f, false);
        HitStopPlan reduced = HitStop.Scale(plan, CombatComfort.ReducedScale);
        Assert.True(reduced.Milliseconds < plan.Milliseconds);
        Assert.InRange(reduced.Milliseconds, 1, plan.Milliseconds / 2);
        Assert.True(reduced.TimeScale > 0.2f, "Reduced Motion must dip the clock, not freeze it");
        Assert.True(reduced.TimeScale < 1f);
    }

    [Fact]
    public void Scale_IsMonotonicInComfort()
    {
        HitStopPlan plan = HitStop.Plan(HitOutcome.PoiseBroken, HitKind.Normal, 20f, true);
        int previous = 0;
        for (float c = 0.1f; c <= 1f; c += 0.1f)
        {
            int ms = HitStop.Scale(plan, c).Milliseconds;
            Assert.True(ms >= previous);
            previous = ms;
        }
    }

    [Fact]
    public void Limiter_GrantsUpToTheBudgetThenRefusesUntilTheWindowSlides()
    {
        var limiter = new HitStopLimiter();
        Assert.Equal(200, limiter.Admit(0, 200));
        Assert.Equal(100, limiter.Admit(50, 200));                       // only 100 left of 300
        Assert.Equal(0, limiter.Admit(100, 200));                        // budget spent
        Assert.Equal(200, limiter.Admit(HitStopLimiter.WindowMs + 1, 200)); // the window slid on
    }

    [Fact]
    public void Limiter_DropsGrantsTooSmallToBeWorthIt()
    {
        var limiter = new HitStopLimiter();
        Assert.Equal(290, limiter.Admit(0, 290));
        Assert.Equal(0, limiter.Admit(10, 100)); // 10 ms left, under the minimum
    }

    [Fact]
    public void Limiter_ResetForgetsEverything()
    {
        var limiter = new HitStopLimiter();
        limiter.Admit(0, 300);
        limiter.Reset();
        Assert.Equal(150, limiter.Admit(1, 150));
    }

    [Fact]
    public void Limiter_AFlurryNeverStallsTheGame()
    {
        var limiter = new HitStopLimiter();
        int total = 0;
        for (ulong t = 0; t < 700; t += 25)
        {
            total += limiter.Admit(t, 150);
        }

        Assert.True(total <= HitStopLimiter.BudgetMs);
    }
}
