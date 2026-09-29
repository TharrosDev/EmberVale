using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Enemies;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules of the offence pass: charge and heavy, the tap/hold decision, the plunge, humanoid
/// attack direction, the reworked input buffer, the commitment helpers and the dragon's sticky arcs.
/// Everything here is Godot-node-free; what needs a physics frame is in tools/combat_offence_probe.gd.
/// </summary>
public class OffenceTests
{
    // ---- charge --------------------------------------------------------------------------------

    [Fact]
    public void Charge_Fraction_IsClamped()
    {
        Assert.Equal(0f, ChargeRules.Fraction(-1f, 1f));
        Assert.Equal(0.5f, ChargeRules.Fraction(0.5f, 1f), 3);
        Assert.Equal(1f, ChargeRules.Fraction(9f, 1f));
        Assert.Equal(0f, ChargeRules.Fraction(1f, 0f));
    }

    [Fact]
    public void Charge_KindSwitchesAtThreshold()
    {
        Assert.Equal(HitKind.Heavy, ChargeRules.KindOf(0f));
        Assert.Equal(HitKind.Heavy, ChargeRules.KindOf(ChargeRules.ChargedFrom - 0.01f));
        Assert.Equal(HitKind.Charged, ChargeRules.KindOf(ChargeRules.ChargedFrom));
        Assert.Equal(HitKind.Charged, ChargeRules.KindOf(1f));
    }

    [Fact]
    public void Charge_ScalesDamagePoiseCostAndSpeed()
    {
        Assert.Equal(1f, ChargeRules.DamageMultiplier(0f, 0.75f));
        Assert.Equal(1.75f, ChargeRules.DamageMultiplier(1f, 0.75f), 3);
        Assert.Equal(1.75f, ChargeRules.DamageMultiplier(5f, 0.75f), 3);
        Assert.True(ChargeRules.PoiseMultiplier(1f, 0.75f) > ChargeRules.DamageMultiplier(1f, 0.75f));
        Assert.Equal(20f, ChargeRules.ReleaseCost(20f, 0f));
        Assert.Equal(30f, ChargeRules.ReleaseCost(20f, 1f), 3);
        Assert.Equal(1.4f, ChargeRules.ReleaseSpeed(1f), 3);
    }

    [Fact]
    public void Charge_HyperarmorOnlyNearFull()
    {
        Assert.False(ChargeRules.GrantsHyperarmor(0.5f));
        Assert.True(ChargeRules.GrantsHyperarmor(ChargeRules.HyperarmorFrom));
    }

    // ---- tap / hold ----------------------------------------------------------------------------

    private static AttackIntent Step(ref AttackInputState s, bool pressed, bool held, bool released,
        bool busy = false, float dt = 0.016f) =>
        s.Step(pressed, held, released, busy, dt, ChargeRules.HoldThreshold);

    [Fact]
    public void Input_TapFromRest_SwingsOnRelease()
    {
        var s = new AttackInputState();
        Assert.Equal(AttackIntent.None, Step(ref s, true, true, false));
        Assert.True(s.IsPending);
        Assert.Equal(AttackIntent.None, Step(ref s, false, true, false));
        Assert.Equal(AttackIntent.Light, Step(ref s, false, false, true));
        Assert.False(s.IsPending);
    }

    [Fact]
    public void Input_SameFrameTap_IsStillATap()
    {
        var s = new AttackInputState();
        Assert.Equal(AttackIntent.Light, Step(ref s, true, false, true));
    }

    [Fact]
    public void Input_ChainPressWhileBusy_FiresAtOnce()
    {
        var s = new AttackInputState();
        Assert.Equal(AttackIntent.Light, Step(ref s, true, true, false, busy: true));
    }

    [Fact]
    public void Input_HoldPastThreshold_ChargesThenReleases()
    {
        var s = new AttackInputState();
        Step(ref s, true, true, false);
        AttackIntent intent = AttackIntent.None;
        for (int i = 0; i < 30 && intent == AttackIntent.None; i++)
        {
            intent = Step(ref s, false, true, false);
        }

        Assert.Equal(AttackIntent.BeginCharge, intent);
        Assert.True(s.IsCharging);
        Assert.Equal(AttackIntent.None, Step(ref s, false, true, false));
        Assert.Equal(AttackIntent.Release, Step(ref s, false, false, true));
        Assert.False(s.IsCharging);
    }

    [Fact]
    public void Input_AbortedCharge_SwallowsItsRelease()
    {
        var s = new AttackInputState();
        Step(ref s, true, true, false);
        while (Step(ref s, false, true, false) != AttackIntent.BeginCharge)
        {
        }

        s.Abort();
        Assert.Equal(AttackIntent.None, Step(ref s, false, true, false));
        Assert.Equal(AttackIntent.None, Step(ref s, false, false, true));

        // ... and the next press is a fresh one.
        Assert.Equal(AttackIntent.None, Step(ref s, true, true, false));
        Assert.True(s.IsPending);
    }

    [Fact]
    public void Input_Reset_ForgetsACharge()
    {
        var s = new AttackInputState();
        Step(ref s, true, true, false);
        while (Step(ref s, false, true, false) != AttackIntent.BeginCharge)
        {
        }

        s.Reset();
        Assert.False(s.IsCharging);
        Assert.Equal(AttackIntent.None, Step(ref s, false, false, true));
    }

    // ---- plunge --------------------------------------------------------------------------------

    [Fact]
    public void Plunge_NeedsAirHeightAndAFoot()
    {
        Assert.False(PlungeRules.CanStart(3f, grounded: true, mounted: false));
        Assert.False(PlungeRules.CanStart(3f, grounded: false, mounted: true));
        Assert.False(PlungeRules.CanStart(PlungeRules.MinHeight - 0.1f, false, false));
        Assert.True(PlungeRules.CanStart(PlungeRules.MinHeight, false, false));
    }

    [Fact]
    public void Plunge_HeightScale_RisesThenCaps()
    {
        Assert.Equal(1f, PlungeRules.HeightScale(0f));
        Assert.Equal(1f, PlungeRules.HeightScale(PlungeRules.MinHeight));
        float mid = PlungeRules.HeightScale(4f);
        Assert.InRange(mid, 1.1f, 1.9f);
        Assert.Equal(1f + PlungeRules.MaxBonus, PlungeRules.HeightScale(PlungeRules.MaxScaledHeight), 3);
        Assert.Equal(1f + PlungeRules.MaxBonus, PlungeRules.HeightScale(200f), 3);
    }

    // ---- direction -----------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 0f, AttackDirection.Neutral)]
    [InlineData(0.3f, 0.2f, AttackDirection.Neutral)]
    [InlineData(0f, -1f, AttackDirection.Forward)]
    [InlineData(0f, 1f, AttackDirection.Back)]
    [InlineData(-1f, 0f, AttackDirection.Left)]
    [InlineData(1f, 0f, AttackDirection.Right)]
    [InlineData(0.6f, -0.7f, AttackDirection.Forward)]
    [InlineData(0.9f, 0.4f, AttackDirection.Right)]
    public void Direction_Resolves(float x, float y, AttackDirection expected)
    {
        Assert.Equal(expected, AttackDirections.Resolve(new Vector2(x, y)));
    }

    [Fact]
    public void Direction_Modifiers_TradeAsDescribed()
    {
        DirectionModifier neutral = AttackDirections.Modifier(AttackDirection.Neutral);
        Assert.Equal(1f, neutral.DamageScale);
        Assert.Equal(Vector3.Zero, neutral.Advance);

        DirectionModifier forward = AttackDirections.Modifier(AttackDirection.Forward);
        Assert.True(forward.DamageScale > 1f);
        Assert.True(forward.Advance.Z < 0f); // forward is -Z

        DirectionModifier back = AttackDirections.Modifier(AttackDirection.Back);
        Assert.True(back.DamageScale < 1f);
        Assert.True(back.Advance.Z > 0f);

        Assert.Equal(
            AttackDirections.Modifier(AttackDirection.Left).Advance.X,
            -AttackDirections.Modifier(AttackDirection.Right).Advance.X);
    }

    // ---- buffer --------------------------------------------------------------------------------

    [Fact]
    public void Buffer_AcceptsOnlyNearTheCancelPoint()
    {
        Assert.True(AttackBuffer.Accepts(0f, AttackBuffer.DefaultLead));
        Assert.True(AttackBuffer.Accepts(AttackBuffer.DefaultLead, AttackBuffer.DefaultLead));
        Assert.False(AttackBuffer.Accepts(AttackBuffer.DefaultLead + 0.01f, AttackBuffer.DefaultLead));
    }

    [Fact]
    public void Buffer_Lifetime_ReachesTheCancelPoint()
    {
        Assert.Equal(0.18, AttackBuffer.Lifetime(0.18f, 0.05f), 3);
        Assert.Equal(0.25 + AttackBuffer.Grace, AttackBuffer.Lifetime(0.18f, 0.25f), 3);
    }

    // ---- timeline and warp ---------------------------------------------------------------------

    private static readonly ActionWindows Shape = new(0.3f, 0.5f, 0.7f, 0.5f, 1f);

    [Fact]
    public void Timeline_SecondsUntilCancel()
    {
        Assert.Equal(0.4f, ActionTimeline.SecondsUntilCancel(0.5f, Shape, 2.0), 3);
        Assert.Equal(0f, ActionTimeline.SecondsUntilCancel(0.7f, Shape, 2.0));
        Assert.Equal(0f, ActionTimeline.SecondsUntilCancel(0.1f, Shape, 0.0));
    }

    [Fact]
    public void Timeline_CommittedRecovery_IsBetweenBlowAndCancel()
    {
        Assert.False(ActionTimeline.InCommittedRecovery(0.4f, Shape));
        Assert.True(ActionTimeline.InCommittedRecovery(0.5f, Shape));
        Assert.True(ActionTimeline.InCommittedRecovery(0.69f, Shape));
        Assert.False(ActionTimeline.InCommittedRecovery(0.7f, Shape));
    }

    [Fact]
    public void Warp_DoesNotChaseSomeoneBehind()
    {
        // Yaw 0 faces -Z.
        Vector3 from = Vector3.Zero;
        Vector3 ahead = new(0f, 0f, -3f);
        Vector3 behind = new(0f, 0f, 3f);
        Assert.True(MotionWarp.Reachable(0f, from, ahead, 30f));
        Assert.False(MotionWarp.Reachable(0f, from, behind, 30f));
        Assert.True(MotionWarp.Reachable(0f, from, from, 30f));
    }

    [Fact]
    public void Warp_AdvanceStep_SpendsAFractionOfTheRemainder()
    {
        Vector3 step = MotionWarp.AdvanceStep(new Vector3(0f, 0f, -1f), 0.25f);
        Assert.Equal(-0.25f, step.Z, 3);
        Assert.Equal(Vector3.Zero, MotionWarp.AdvanceStep(new Vector3(1f, 0f, 0f), 0f));
    }

    [Fact]
    public void Turn_IsCappedPerSecond()
    {
        float yaw = MotionWarp.LimitedYaw(0f, Mathf.DegToRad(30f), 90f, 1.0 / 60.0);
        Assert.Equal(Mathf.DegToRad(1.5f), yaw, 3);
        Assert.Equal(0f, MotionWarp.LimitedYaw(0f, 1f, 0f, 0.1));
        Assert.Equal(1f, MotionWarp.LimitedYaw(0f, 1f, -1f, 0.1));
        Assert.Equal(-Mathf.DegToRad(1.5f), MotionWarp.LimitedYaw(0f, -1f, 90f, 1.0 / 60.0), 3);
    }

    // ---- AI selection --------------------------------------------------------------------------

    [Fact]
    public void Selection_RepeatCarriesLessWeight()
    {
        var chain = new[]
        {
            new ActionSelection.Candidate(0f, 5f, 1f),
            new ActionSelection.Candidate(0f, 5f, 1f),
        };

        // Without a penalty a roll of 0.4 picks link 0; with link 0 just used it is 1/1.45 of the
        // pool, so the same roll now lands on it less often: 0.4 * 1.45 = 0.58 > 0.45.
        Assert.Equal(0, ActionSelection.Choose(chain, 2f, 0.4f));
        Assert.Equal(1, ActionSelection.Choose(chain, 2f, 0.4f, lastPick: 0, repeatWeight: 0.45f));
    }

    [Fact]
    public void Selection_RepeatPenalty_NeverRemovesTheOnlyOption()
    {
        var chain = new[] { new ActionSelection.Candidate(0f, 5f, 1f) };
        Assert.Equal(0, ActionSelection.Choose(chain, 2f, 0.5f, lastPick: 0, repeatWeight: 0f));
    }

    // ---- dragon --------------------------------------------------------------------------------

    [Fact]
    public void Dragon_KeepsItsArcNearTheBoundary()
    {
        // 55 degrees is the wing's by the plain rule ...
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(55f));

        // ... but a dragon already armed with the bite keeps it until the target is clearly past.
        Assert.Equal(DragonAttack.Bite, DragonMelee.Choose(55f, DragonAttack.Bite));
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(DragonMelee.BiteHalfAngle + DragonMelee.Hysteresis + 2f, DragonAttack.Bite));

        // A wing holds across the tail boundary the same way, and the sign is ignored.
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(-135f, DragonAttack.Wing));
        Assert.Equal(DragonAttack.Tail, DragonMelee.Choose(150f, DragonAttack.Wing));
    }
}
