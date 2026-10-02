using Embervale.Combat;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the pure rules of the committed cast (magic upgrade 2026-09, casting core).</summary>
public class SpellRulesTests
{
    [Fact]
    public void Shape_WithNothingAuthored_IsTheLegacyClipDrivenShape()
    {
        CastShape instant = SpellRules.Shape(0f, 0f, sustained: false);
        Assert.Equal(new CastShape(0f, 0.7f, 0.45f, 0.55f, 0.7f), instant);
        CastShape channel = SpellRules.Shape(0f, 0f, sustained: true);
        Assert.Equal(new CastShape(0f, 0.45f, 0.2f, 0.3f, 0.35f), channel);
    }

    [Theory]
    [InlineData(0.15f, 0.3f)]
    [InlineData(0.45f, 0.4f)]
    [InlineData(0.9f, 0.6f)]
    public void Shape_DerivesTheWindupAndRecoveryFromTheSpell(float windup, float recovery)
    {
        CastShape s = SpellRules.Shape(windup, recovery, sustained: false);
        Assert.Equal(windup + SpellRules.ReleaseSpanSeconds + recovery, s.Duration, 3);
        Assert.Equal(s.Duration, s.FallbackDuration);
        Assert.Equal(windup, SpellRules.WindupOf(s), 3);
        Assert.Equal((windup + SpellRules.ReleaseSpanSeconds) / s.Duration, s.ActiveTo, 4);
        Assert.Equal(1f, s.CancelFrom);
        Assert.True(s.ActiveFrom < s.ActiveTo && s.ActiveTo < s.CancelFrom);
    }

    [Fact]
    public void Shape_OneAuthoredNumberFillsTheOtherFromTheLegacyShape()
    {
        Assert.Equal(0.5f, SpellRules.WindupOf(SpellRules.Shape(0.5f, 0f, false)), 3);
        CastShape s = SpellRules.Shape(0f, 0.5f, false);
        Assert.Equal(SpellRules.LegacyInstantWindup, SpellRules.WindupOf(s), 3);
    }

    [Theory]
    [InlineData(true, false, false, true, false, true)]    // stagger cancels
    [InlineData(true, false, false, false, false, false)]  // not interruptible: stagger does nothing
    [InlineData(true, false, false, true, true, false)]    // barkskin lets a cast finish through a stagger
    [InlineData(false, true, false, false, true, true)]    // silence always cancels
    [InlineData(false, false, true, false, true, true)]    // stun always cancels
    [InlineData(false, false, false, true, false, false)]  // nothing happening
    public void Interrupts_FollowsThePlaybookRules(
        bool staggered, bool silenced, bool stunned, bool interruptible, bool armoured, bool expected)
    {
        Assert.Equal(expected, SpellRules.Interrupts(staggered, silenced, stunned, interruptible, armoured));
    }

    [Fact]
    public void ACasterCannotBeginWhileSilencedOrStunned()
    {
        Assert.True(SpellRules.CanBegin(false, false));
        Assert.False(SpellRules.CanBegin(true, false));
        Assert.False(SpellRules.CanBegin(false, true));
    }

    [Fact]
    public void AnInterruptedCastRefundsHalfItsMana()
    {
        Assert.Equal(9f, SpellRules.InterruptRefund(18f));
        Assert.Equal(0f, SpellRules.InterruptRefund(-4f));
    }

    [Fact]
    public void AHealthCostIsRefusedRatherThanKillingTheCaster()
    {
        Assert.True(SpellRules.CanPayHealth(50f, 20f));
        Assert.False(SpellRules.CanPayHealth(20f, 20f));
        Assert.False(SpellRules.CanPayHealth(5f, 20f));
        Assert.True(SpellRules.CanPayHealth(1f, 0f));
    }

    [Fact]
    public void ConsumedStacksAddDamageOnlyWhenThereAreStacksAndABonus()
    {
        Assert.Equal(1f, SpellRules.ConsumeMultiplier(0, 0.5f));
        Assert.Equal(1f, SpellRules.ConsumeMultiplier(3, 0f));
        Assert.Equal(2.5f, SpellRules.ConsumeMultiplier(3, 0.5f));
    }

    [Fact]
    public void AFullerChargePiercesMoreFoes()
    {
        Assert.Equal(1, SpellRules.PierceCount(1, 2, 0f));
        Assert.Equal(2, SpellRules.PierceCount(1, 2, 0.5f));
        Assert.Equal(3, SpellRules.PierceCount(1, 2, 1f));
        Assert.Equal(3, SpellRules.PierceCount(1, 2, 5f));
        Assert.Equal(0, SpellRules.PierceCount(0, 0, 1f));
    }

    [Fact]
    public void AFullerChargeBurnsLongerWithinTheAuthoredRange()
    {
        Assert.Equal(1f, SpellRules.StatusDurationMultiplier(0f, 1f));
        Assert.Equal(1.5f, SpellRules.StatusDurationMultiplier(0.5f, 1f));
        Assert.Equal(2f, SpellRules.StatusDurationMultiplier(1f, 1f));
        Assert.Equal(2f, SpellRules.StatusDurationMultiplier(3f, 1f));
        Assert.Equal(1f, SpellRules.StatusDurationMultiplier(-3f, 1f));
        Assert.Equal(1f, SpellRules.StatusDurationMultiplier(1f, -1f));
    }

    [Fact]
    public void AMeteorCentreCrushesButTheEdgeOfAFullChargeCanBeBlocked()
    {
        Assert.True(SpellRules.IsDirectImpact(0.8f, 0.8f));
        Assert.False(SpellRules.IsDirectImpact(1.2f, 0.8f));
        var edge = new DamagePacket(10f, DamageType.Fire, null, false, 5f, HitKind.Spell, 1f)
        {
            GuardCrushOverride = SpellRules.IsDirectImpact(1.2f, 0.8f),
        };
        Assert.Equal(1f, edge.Charge);
        Assert.False(edge.GuardCrushOverride);
        Assert.Null(new DamagePacket(10f, DamageType.Fire, null, false, 5f).GuardCrushOverride);
    }

    [Fact]
    public void ChannelTimingContainsAuthoredWindupAndRecovery()
    {
        CastShape shape = SpellRules.Shape(0.25f, 0.2f, sustained: true);
        Assert.Equal(0.25f, SpellRules.WindupOf(shape), 4);
        Assert.Equal(0.2f, shape.Duration * (1f - shape.ActiveTo), 4);
        Assert.Equal(1f, shape.CancelFrom);
    }

    [Fact]
    public void ABarrierInterceptsAtItsNearFaceRatherThanItsCentrePlane()
    {
        // An arrow's world ray ends at z=0.25, the near face of a 0.5 m wall.
        Assert.Equal(1f, SpellRules.BarrierEntry(0f, 1f, 1f, 0f, 1f, 0.25f, 4f, 2.6f, 0.5f, 0f), 4);
        // A radius .25 bolt overlaps the wall at z=.5 before its centre reaches the face.
        Assert.Equal(1f, SpellRules.BarrierEntry(0f, 1f, 1f, 0f, 1f, 0.5f, 4f, 2.6f, 0.5f, 0.25f), 4);
        Assert.Equal(-1f, SpellRules.BarrierEntry(3f, 1f, 1f, 3f, 1f, -1f, 4f, 2.6f, 0.5f, 0f));
        Assert.Equal(-1f, SpellRules.BarrierEntry(0f, 3f, 1f, 0f, 3f, -1f, 4f, 2.6f, 0.5f, 0f));
        // Hits from behind, parallel steps, and starts inside the volume use the same geometry.
        Assert.Equal(0.375f, SpellRules.BarrierEntry(0f, 1f, -1f, 0f, 1f, 1f, 4f, 2.6f, 0.5f, 0f), 4);
        Assert.Equal(0f, SpellRules.BarrierEntry(0f, 1f, 0f, 1f, 1f, 0f, 4f, 2.6f, 0.5f, 0f));
        Assert.Equal(-1f, SpellRules.BarrierEntry(0f, 1f, 1f, 1f, 1f, 1f, 4f, 2.6f, 0.5f, 0f));
    }

    [Fact]
    public void BlinkStopsShortOfAWallAndCostsLessForAShorterJump()
    {
        float travelled = SpellRules.TravelDistance(8f, 3f, 0.5f);
        Assert.Equal(2.5f, travelled);
        Assert.Equal(8f, SpellRules.TravelDistance(8f, -1f, 0.5f));
        Assert.Equal(0f, SpellRules.TravelDistance(8f, 0.2f, 0.5f));

        float full = SpellRules.BlinkCost(20f, 8f, 8f);
        float half = SpellRules.BlinkCost(20f, 4f, 8f);
        float none = SpellRules.BlinkCost(20f, 0f, 8f);
        Assert.Equal(20f, full);
        Assert.True(none < half && half < full);
        Assert.Equal(20f * SpellRules.BlinkMinCostFraction, none, 3);
    }

    [Fact]
    public void PlaceDistanceIsClampedToTheRange()
    {
        Assert.Equal(18f, SpellRules.ClampPlaceDistance(40f, 18f));
        Assert.Equal(7f, SpellRules.ClampPlaceDistance(7f, 18f));
        Assert.Equal(0f, SpellRules.ClampPlaceDistance(-3f, 18f));
    }

    [Fact]
    public void ImpactWeight_ScalesHitStopAndShakeAndDefaultsToTheOldWeight()
    {
        Assert.Equal(1f, SpellRules.HitStopScale(0.5f));
        Assert.Equal(0f, SpellRules.HitStopScale(0f));
        Assert.Equal(2f, SpellRules.HitStopScale(9f));
        Assert.True(SpellRules.ShakeTrauma(1f) < ShakeMath.CritTrauma);
        Assert.Equal(0f, SpellRules.ShakeTrauma(0f));
    }

    [Fact]
    public void ABarrierFaceIsAWidthByHeightRectangle()
    {
        Assert.True(SpellRules.OnBarrierFace(1.9f, 1f, 4f, 2.6f));
        Assert.False(SpellRules.OnBarrierFace(2.1f, 1f, 4f, 2.6f));
        Assert.False(SpellRules.OnBarrierFace(0f, 3f, 4f, 2.6f));
        Assert.False(SpellRules.OnBarrierFace(0f, -0.1f, 4f, 2.6f));
    }

    [Fact]
    public void PlaneCrossing_FindsWhereASegmentPassesThrough()
    {
        Assert.Equal(0.5f, SpellRules.PlaneCrossing(1f, -1f), 4);
        Assert.Equal(-1f, SpellRules.PlaneCrossing(1f, 2f));
        Assert.Equal(-1f, SpellRules.PlaneCrossing(0f, 0f));
    }

    [Fact]
    public void ASpellBlowIsNeverParryable_AndAFullChargeCrushesAGuard()
    {
        Assert.False(DefenceRules.Profile(HitKind.Spell, 1f).Parryable);
        Assert.False(DefenceRules.Profile(HitKind.Spell, 0f).CrushesGuard);
        Assert.False(DefenceRules.Profile(HitKind.Spell, 0.5f).CrushesGuard);
        Assert.True(DefenceRules.Profile(HitKind.Spell, 1f).CrushesGuard);
        Assert.Equal(1f, DefenceRules.Profile(HitKind.Spell, 0f).DamageMultiplier);
    }

    [Fact]
    public void UnblockableIsAPacketFlagThatDefaultsOff()
    {
        var packet = new DamagePacket(10f, DamageType.Fire, null, false, 5f, HitKind.Spell, 0.5f);
        Assert.False(packet.Unblockable);
        Assert.True((packet with { Unblockable = true }).Unblockable);
    }
}
