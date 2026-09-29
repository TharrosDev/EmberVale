using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure half of hit-confirm: one outcome per blow (precedence), the weight of a blow, and
/// that every outcome gets its own sound, spark and screen state so eight different things do not
/// all read as a white puff.
/// </summary>
public class CombatHitConfirmTests
{
    private static HitFacts Facts(
        bool damage = true, bool crit = false, bool blocked = false, bool guardBroken = false,
        bool staggered = false, bool parried = false, bool resisted = false) =>
        new(damage, crit, blocked, guardBroken, staggered, parried, resisted);

    [Fact]
    public void Resolve_PlainHit_IsHit() => Assert.Equal(HitOutcome.Hit, HitOutcomes.Resolve(Facts()));

    [Fact]
    public void Resolve_Precedence_ParryGuardBreakCritStaggerBlockResist()
    {
        Assert.Equal(HitOutcome.Parried,
            HitOutcomes.Resolve(Facts(parried: true, guardBroken: true, crit: true, blocked: true)));
        Assert.Equal(HitOutcome.GuardBroken,
            HitOutcomes.Resolve(Facts(guardBroken: true, crit: true, staggered: true)));
        Assert.Equal(HitOutcome.Critical, HitOutcomes.Resolve(Facts(crit: true, staggered: true, blocked: true)));
        Assert.Equal(HitOutcome.PoiseBroken, HitOutcomes.Resolve(Facts(staggered: true, blocked: true)));
        Assert.Equal(HitOutcome.Blocked, HitOutcomes.Resolve(Facts(blocked: true, resisted: true)));
        Assert.Equal(HitOutcome.Resisted, HitOutcomes.Resolve(Facts(resisted: true)));
    }

    [Fact]
    public void Resolve_IsIndependentOfTheOrderTheFactsArrived()
    {
        // The director gathers facts in any order; the outcome is a function of the set.
        HitFacts a = Facts(crit: true) with { GuardBroken = true };
        HitFacts b = Facts(guardBroken: true) with { Crit = true };
        Assert.Equal(HitOutcomes.Resolve(a), HitOutcomes.Resolve(b));
    }

    [Theory]
    [InlineData(0.3f, true)]
    [InlineData(0.6f, true)]
    [InlineData(0.61f, false)]
    [InlineData(1f, false)]
    public void IsResisted_TakesSixtyPercentOrLess(float multiplier, bool expected) =>
        Assert.Equal(expected, HitOutcomes.IsResisted(multiplier));

    [Fact]
    public void InferKind_DeclaredWins_ThenChargeThenLastAction()
    {
        Assert.Equal(HitKind.Riposte, HitOutcomes.InferKind(HitKind.Riposte, ActionKind.HeavyAttack, 1f));
        Assert.Equal(HitKind.Charged, HitOutcomes.InferKind(HitKind.Normal, ActionKind.Attack, 0.6f));
        Assert.Equal(HitKind.Heavy, HitOutcomes.InferKind(HitKind.Normal, ActionKind.HeavyAttack, 0f));
        Assert.Equal(HitKind.Ranged, HitOutcomes.InferKind(HitKind.Normal, ActionKind.Ranged, 0f));
        Assert.Equal(HitKind.Spell, HitOutcomes.InferKind(HitKind.Normal, ActionKind.Cast, 0f));
        Assert.Equal(HitKind.Normal, HitOutcomes.InferKind(HitKind.Normal, ActionKind.Attack, 0f));
        Assert.Equal(HitKind.Normal, HitOutcomes.InferKind(HitKind.Normal, null, 0f));
    }

    [Fact]
    public void KindWeight_HeavyOutweighsLightOutweighsRanged()
    {
        Assert.True(HitOutcomes.KindWeight(HitKind.Charged) > HitOutcomes.KindWeight(HitKind.Heavy));
        Assert.True(HitOutcomes.KindWeight(HitKind.Heavy) > HitOutcomes.KindWeight(HitKind.Normal));
        Assert.True(HitOutcomes.KindWeight(HitKind.Normal) > HitOutcomes.KindWeight(HitKind.Ranged));
        Assert.True(HitOutcomes.KindWeight(HitKind.Ranged) > HitOutcomes.KindWeight(HitKind.Spell));
    }

    [Fact]
    public void EveryOutcome_HasADistinctSoundAndSpark()
    {
        var cues = new HashSet<CuePlan>();
        var sparks = new HashSet<SparkPlan>();
        foreach (HitOutcome outcome in System.Enum.GetValues<HitOutcome>())
        {
            Assert.True(cues.Add(CombatFx.Plan(outcome, HitKind.Normal)), $"{outcome} sounds like another outcome");
            Assert.True(sparks.Add(CombatFx.Spark(outcome, 10f)), $"{outcome} looks like another outcome");
        }
    }

    [Fact]
    public void Sound_UsesOnlyShippedCues()
    {
        string[] shipped = { CombatFx.HitCue, CombatFx.CritCue, CombatFx.BlockCue, CombatFx.SwingCue };
        foreach (HitOutcome outcome in System.Enum.GetValues<HitOutcome>())
        {
            Assert.Contains(CombatFx.Plan(outcome, HitKind.Normal).CueId, shipped);
        }
    }

    [Fact]
    public void ParryGuardBreakAndPoiseBreak_ThrowAShockRing()
    {
        Assert.True(CombatFx.Spark(HitOutcome.Parried, 0f).Ring);
        Assert.True(CombatFx.Spark(HitOutcome.GuardBroken, 20f).Ring);
        Assert.True(CombatFx.Spark(HitOutcome.PoiseBroken, 20f).Ring);
        Assert.False(CombatFx.Spark(HitOutcome.Hit, 20f).Ring);
        Assert.False(CombatFx.Spark(HitOutcome.Blocked, 20f).Ring);
    }

    [Fact]
    public void HeavyKinds_SoundLowerAndLouder()
    {
        CuePlan normal = CombatFx.Plan(HitOutcome.Hit, HitKind.Normal);
        CuePlan heavy = CombatFx.Plan(HitOutcome.Hit, HitKind.Heavy);
        CuePlan arrow = CombatFx.Plan(HitOutcome.Hit, HitKind.Ranged);
        Assert.True(heavy.PitchScale < normal.PitchScale);
        Assert.True(heavy.VolumeDb > normal.VolumeDb);
        Assert.True(arrow.PitchScale > normal.PitchScale);
        Assert.True(arrow.VolumeDb < normal.VolumeDb);
    }

    [Fact]
    public void ForOutcome_MapsThePlayersSideOfEachOutcome()
    {
        // The player parried (as defender).
        Assert.Equal(CombatFeedback.Parry,
            CombatFeedbackFx.ForOutcome(HitOutcome.Parried, HitKind.Normal, true, false, true));
        // The player was parried (as attacker): they are the one staggered.
        Assert.Equal(CombatFeedback.Stagger,
            CombatFeedbackFx.ForOutcome(HitOutcome.Parried, HitKind.Normal, true, true, false));
        Assert.Equal(CombatFeedback.GuardBroken,
            CombatFeedbackFx.ForOutcome(HitOutcome.GuardBroken, HitKind.Normal, false, false, true));
        Assert.Equal(CombatFeedback.Break,
            CombatFeedbackFx.ForOutcome(HitOutcome.GuardBroken, HitKind.Normal, false, true, false));
        Assert.Equal(CombatFeedback.Riposte,
            CombatFeedbackFx.ForOutcome(HitOutcome.Critical, HitKind.Riposte, false, true, false));
        Assert.Equal(CombatFeedback.Backstab,
            CombatFeedbackFx.ForOutcome(HitOutcome.Critical, HitKind.Backstab, false, true, false));
        Assert.Equal(CombatFeedback.Crit,
            CombatFeedbackFx.ForOutcome(HitOutcome.Critical, HitKind.Normal, false, true, false));
        Assert.Equal(CombatFeedback.Block,
            CombatFeedbackFx.ForOutcome(HitOutcome.Blocked, HitKind.Normal, false, false, true));
        Assert.Equal(CombatFeedback.Stagger,
            CombatFeedbackFx.ForOutcome(HitOutcome.PoiseBroken, HitKind.Normal, true, false, true));
        Assert.Equal(CombatFeedback.Break,
            CombatFeedbackFx.ForOutcome(HitOutcome.PoiseBroken, HitKind.Normal, true, true, false));
    }

    [Fact]
    public void ForOutcome_OrdinaryHitsAndOthersBlowsEarnNoWord()
    {
        Assert.Null(CombatFeedbackFx.ForOutcome(HitOutcome.Hit, HitKind.Normal, false, true, false));
        Assert.Null(CombatFeedbackFx.ForOutcome(HitOutcome.Hit, HitKind.Normal, false, false, true));
        Assert.Null(CombatFeedbackFx.ForOutcome(HitOutcome.Blocked, HitKind.Normal, false, true, false));
        Assert.Null(CombatFeedbackFx.ForOutcome(HitOutcome.Critical, HitKind.Normal, false, false, true));
    }

    [Fact]
    public void EveryState_HasAnAppendOnlyDistinctTintAndAWord()
    {
        var tints = new HashSet<(float, float, float)>();
        foreach (CombatFeedback state in System.Enum.GetValues<CombatFeedback>())
        {
            Assert.True(tints.Add(CombatFeedbackFx.Tint(state)), $"{state} shares a tint");
            Assert.False(string.IsNullOrEmpty(CombatFeedbackFx.WordKey(state)));
            Assert.True(CombatFeedbackFx.PeakAlpha(state) > 0f);
        }

        Assert.Equal(0, (int)CombatFeedback.Crit);
        Assert.Equal(3, (int)CombatFeedback.Parry);
    }

    [Fact]
    public void FlashAlpha_FollowsTheSliderAndZeroLeavesNoFlash()
    {
        Assert.Equal(0f, CombatFeedbackFx.FlashAlpha(CombatFeedback.Parry, 0f));
        Assert.Equal(CombatFeedbackFx.PeakAlpha(CombatFeedback.Parry), CombatFeedbackFx.FlashAlpha(CombatFeedback.Parry, 1f));
        Assert.True(CombatFeedbackFx.FlashAlpha(CombatFeedback.Parry, 0.25f) <
                    CombatFeedbackFx.FlashAlpha(CombatFeedback.Parry, 1f));
    }

    [Fact]
    public void FlashGate_HoldsBackWeakerFlashesButNeverAStrongerOne()
    {
        var gate = new FlashGate();
        Assert.True(gate.Allow(10.0, CombatFeedback.Block));
        Assert.False(gate.Allow(10.1, CombatFeedback.Block));   // inside the interval
        gate.Reset();
        Assert.True(gate.Allow(20.0, CombatFeedback.Block));
        Assert.True(gate.Allow(20.1, CombatFeedback.Parry));    // stronger always gets through
        Assert.False(gate.Allow(20.2, CombatFeedback.Crit));    // weaker than the parry, too soon
        Assert.True(gate.Allow(20.6, CombatFeedback.Crit));     // interval elapsed
    }

    [Fact]
    public void FlashGate_NeverExceedsThreeAFlashesASecondForOneState()
    {
        var gate = new FlashGate();
        int fired = 0;
        for (int i = 0; i < 100; i++)
        {
            if (gate.Allow(i * 0.05, CombatFeedback.Block))
            {
                fired++;
            }
        }

        // 100 attempts over five seconds: at most one per 0.33 s.
        Assert.InRange(fired, 1, 16);
    }
}
