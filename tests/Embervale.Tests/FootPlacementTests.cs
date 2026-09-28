using Embervale.Animation;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Foot placement — the rules that keep a character standing on the ground it is actually on.
///
/// ⚠️ <b>Nearly every test here is about when NOT to correct.</b> An unbounded, always-on foot IK is
/// worse than none: it follows a raycast off a cliff edge and stretches the leg to the valley floor,
/// it fights a jump by reaching for ground that is not there, and it pays two raycasts a frame for
/// every actor in a loaded region. The limits and the disable conditions are the feature.
/// </summary>
public class FootPlacementTests
{
    [Fact]
    public void AFootAboveItsGroundIsBroughtDownAndOneBelowIsLifted()
    {
        Assert.Equal(-0.2f, FootPlacement.FootLift(1.0f, 0.8f, 0.35f, 0.35f), 4);
        Assert.Equal(0.2f, FootPlacement.FootLift(0.8f, 1.0f, 0.35f, 0.35f), 4);
    }

    [Fact]
    public void TheCorrectionIsClampedInBothDirections()
    {
        // A ray down a cliff edge reports ground five metres below. Reaching for it stretches the
        // leg through the hip; refusing is the honest answer.
        Assert.Equal(0.35f, FootPlacement.FootLift(0f, 9f, 0.35f, 0.35f), 4);
        Assert.Equal(-0.35f, FootPlacement.FootLift(9f, 0f, 0.35f, 0.35f), 4);
    }

    [Fact]
    public void APerfectlyPlacedFootIsLeftAlone() =>
        Assert.Equal(0f, FootPlacement.FootLift(1f, 1f, 0.35f, 0.35f), 5);

    [Fact]
    public void ThePelvisDropsToTheLowerFoot()
    {
        // On a slope both feet want different heights. Dropping the hips by the deepest requirement
        // keeps the low knee bent; without it that leg goes straight and the character tiptoes.
        Assert.Equal(-0.2f, FootPlacement.PelvisDrop(-0.2f, 0.05f, 0.35f), 4);
        Assert.Equal(-0.3f, FootPlacement.PelvisDrop(-0.3f, -0.1f, 0.35f), 4);
    }

    [Fact]
    public void ThePelvisNeverRises()
    {
        // Two feet that both want lifting means the ground came UP to meet them; raising the hips as
        // well would launch the character off the slope.
        Assert.Equal(0f, FootPlacement.PelvisDrop(0.2f, 0.1f, 0.35f), 5);
    }

    [Fact]
    public void ThePelvisDropIsClamped() =>
        Assert.Equal(-0.35f, FootPlacement.PelvisDrop(-9f, -9f, 0.35f), 4);

    [Fact]
    public void PlacementIsOffWhileAirborne()
    {
        // ⚠️ The important one. A jumping character has no ground worth meeting, and a correction
        // that keeps reaching down turns a jump into a stretch.
        Assert.False(FootPlacement.ShouldPlace(
            grounded: false, acting: false, visible: true, distanceToCamera: 1f, maxDistance: 25f));
    }

    [Fact]
    public void PlacementIsOffDuringAnAction()
    {
        // A warping or root-motion action already owns the body's position; IK fighting it shimmers.
        Assert.False(FootPlacement.ShouldPlace(
            grounded: true, acting: true, visible: true, distanceToCamera: 1f, maxDistance: 25f));
    }

    [Fact]
    public void PlacementIsOffWhenNobodyCanSeeIt()
    {
        // Two raycasts a frame per actor across a loaded region is exactly the "expensive IK at
        // unlimited range" the performance rule warns about.
        Assert.False(FootPlacement.ShouldPlace(
            grounded: true, acting: false, visible: false, distanceToCamera: 1f, maxDistance: 25f));
        Assert.False(FootPlacement.ShouldPlace(
            grounded: true, acting: false, visible: true, distanceToCamera: 40f, maxDistance: 25f));
    }

    [Fact]
    public void PlacementIsOnForAGroundedVisibleCharacterStandingStill() =>
        Assert.True(FootPlacement.ShouldPlace(
            grounded: true, acting: false, visible: true, distanceToCamera: 5f, maxDistance: 25f));

    [Fact]
    public void TheWeightFadesRatherThanSnapping()
    {
        // Popping the correction on and off at a boundary is more visible than the defect it fixes.
        float w = 0f;
        w = FootPlacement.StepWeight(w, wanted: true, delta: 0.05f, seconds: 0.15f);
        Assert.InRange(w, 0.01f, 0.99f);

        for (int i = 0; i < 10; i++)
        {
            w = FootPlacement.StepWeight(w, wanted: true, delta: 0.05f, seconds: 0.15f);
        }

        Assert.Equal(1f, w, 4);
    }

    [Fact]
    public void AZeroBlendSnaps()
    {
        Assert.Equal(1f, FootPlacement.StepWeight(0f, wanted: true, delta: 0.016f, seconds: 0f), 4);
        Assert.Equal(0f, FootPlacement.StepWeight(1f, wanted: false, delta: 0.016f, seconds: 0f), 4);
    }

    [Fact]
    public void SlopeAlignmentIsLimited()
    {
        // A 60 degree face must not snap the ankle 60 degrees; a leg does not do that.
        Basis flat = Basis.Identity;
        Vector3 steep = new Vector3(1f, 1f, 0f).Normalized();
        Basis aligned = FootPlacement.AlignToSlope(flat, steep, maxDegrees: 10f, weight: 1f);
        float moved = flat.Y.AngleTo(aligned.Y);
        Assert.InRange(Mathf.RadToDeg(moved), 0f, 10.01f);
    }

    [Fact]
    public void SlopeAlignmentDoesNothingAtZeroWeightOrOnFlatGround()
    {
        Basis flat = Basis.Identity;
        Assert.Equal(flat, FootPlacement.AlignToSlope(flat, Vector3.Up, 35f, weight: 0f));
        Assert.Equal(flat, FootPlacement.AlignToSlope(flat, Vector3.Up, 35f, weight: 1f));
        Assert.Equal(flat, FootPlacement.AlignToSlope(flat, Vector3.Zero, 35f, weight: 1f));
    }

    // --- The 2026-09 upgrade: easing, planted weight, the two-bone solve and the new disables. ---

    [Fact]
    public void PlacementIsOffWhenMountedOrRolling()
    {
        // A rider's legs belong to the saddle; a roll tumbles the body through the ground plane.
        Assert.False(FootPlacement.ShouldPlace(true, false, true, 1f, 25f, mounted: true));
        Assert.False(FootPlacement.ShouldPlace(true, false, true, 1f, 25f, dashing: true));
        Assert.True(FootPlacement.ShouldPlace(true, false, true, 1f, 25f, mounted: false, dashing: false));
    }

    [Fact]
    public void SmoothingIsFrameRateIndependent()
    {
        // Two 60 fps frames and one 30 fps frame cover the same time and must land in the same
        // place. The old per-frame Lerp settled twice as fast at 120 fps as at 60.
        float twoSmall = FootPlacement.Smooth(FootPlacement.Smooth(0f, 1f, 12f, 1f / 60f), 1f, 12f, 1f / 60f);
        float oneLarge = FootPlacement.Smooth(0f, 1f, 12f, 1f / 30f);
        Assert.Equal(oneLarge, twoSmall, 4);
        Assert.InRange(oneLarge, 0.01f, 0.99f);
    }

    [Fact]
    public void SmoothingNeverOvershootsAndZeroSharpnessSnaps()
    {
        Assert.InRange(FootPlacement.Smooth(0f, 1f, 12f, 10f), 0.99f, 1f);
        Assert.Equal(1f, FootPlacement.Smooth(0f, 1f, 0f, 0.016f), 5);
        Assert.Equal(0.3f, FootPlacement.Smooth(0.3f, 1f, 12f, 0f), 5);
    }

    [Fact]
    public void OnlyAPlantedFootIsLaidFlat()
    {
        // A foot mid-swing tilted to the ground under it digs its toe in on every uphill stride.
        Assert.Equal(1f, FootPlacement.Planted(0f, 0.03f, 0.15f), 4);
        Assert.Equal(0f, FootPlacement.Planted(0.2f, 0.03f, 0.15f), 4);
        Assert.InRange(FootPlacement.Planted(0.09f, 0.03f, 0.15f), 0.01f, 0.99f);
    }

    [Fact]
    public void SlopeRotationTipsUpTowardTheNormalWithinTheLimit()
    {
        Vector3 normal = new Vector3(0f, 1f, 1f).Normalized(); // a 45 degree slope
        Quaternion full = FootPlacement.SlopeRotation(Vector3.Up, normal, 90f, 1f);
        Assert.True((full * Vector3.Up).IsEqualApprox(normal));

        Quaternion limited = FootPlacement.SlopeRotation(Vector3.Up, normal, 20f, 1f);
        Assert.Equal(20f, Mathf.RadToDeg(Vector3.Up.AngleTo(limited * Vector3.Up)), 2);

        Assert.Equal(Quaternion.Identity, FootPlacement.SlopeRotation(Vector3.Up, normal, 35f, 0f));
    }

    private static readonly Vector3 Hip = new(0f, 1f, 0f);
    private static readonly Vector3 Knee = new(0f, 0.55f, 0.06f);
    private static readonly Vector3 Ankle = new(0f, 0.1f, 0f);

    private static (Vector3 Knee, Vector3 Foot) Apply(Vector3 hip, Vector3 knee, Vector3 foot,
        Quaternion upper, Quaternion lower)
    {
        Vector3 newKnee = hip + (upper * (knee - hip));
        Vector3 newFoot = newKnee + ((upper * lower) * (foot - knee));
        return (newKnee, newFoot);
    }

    [Theory]
    [InlineData(0f, 0.3f, 0f)]    // lifted onto a stair
    [InlineData(0f, 0.12f, 0.08f)] // forward, near full reach
    [InlineData(0.08f, 0.2f, -0.1f)]
    public void TheTwoBoneSolvePutsTheAnkleOnTheTarget(float x, float y, float z)
    {
        var target = new Vector3(x, y, z);
        Assert.True(FootPlacement.SolveTwoBone(Hip, Knee, Ankle, target, Vector3.Forward,
            out Quaternion upper, out Quaternion lower));

        (Vector3 knee, Vector3 foot) = Apply(Hip, Knee, Ankle, upper, lower);
        Assert.True(foot.DistanceTo(target) < 0.002f, $"foot {foot} missed {target}");

        // Bones keep their length: the leg is rotated, never stretched.
        Assert.Equal((Knee - Hip).Length(), (knee - Hip).Length(), 4);
        Assert.Equal((Ankle - Knee).Length(), (foot - knee).Length(), 4);
    }

    [Fact]
    public void TheKneeKeepsBendingTheWayItWasAnimated()
    {
        // Lifting the foot bends the knee further, and forward (+Z here) — never backwards.
        FootPlacement.SolveTwoBone(Hip, Knee, Ankle, new Vector3(0f, 0.35f, 0f), Vector3.Back,
            out Quaternion upper, out Quaternion lower);
        (Vector3 knee, _) = Apply(Hip, Knee, Ankle, upper, lower);
        Assert.True(knee.Z > Knee.Z, $"knee {knee} should bend further forward");
    }

    [Fact]
    public void AnUnreachableTargetStraightensTheLegTowardItRatherThanTearingIt()
    {
        var target = new Vector3(0f, -2f, 0f);
        FootPlacement.SolveTwoBone(Hip, Knee, Ankle, target, Vector3.Forward,
            out Quaternion upper, out Quaternion lower);
        (Vector3 knee, Vector3 foot) = Apply(Hip, Knee, Ankle, upper, lower);

        float reach = (Knee - Hip).Length() + (Ankle - Knee).Length();
        Assert.InRange((foot - Hip).Length(), reach * 0.99f, reach + 0.0001f);
        Assert.True((foot - Hip).Normalized().Dot(Vector3.Down) > 0.999f);
        Assert.Equal((Knee - Hip).Length(), (knee - Hip).Length(), 4);
    }

    [Fact]
    public void AStraightLegBendsTowardThePole()
    {
        // Dead straight, the bend plane is undefined; the pole says which way the knee goes.
        var hip = new Vector3(0f, 1f, 0f);
        var knee = new Vector3(0f, 0.55f, 0f);
        var foot = new Vector3(0f, 0.1f, 0f);
        var pole = new Vector3(0f, 0f, 1f);
        Assert.True(FootPlacement.SolveTwoBone(hip, knee, foot, new Vector3(0f, 0.3f, 0f), pole,
            out Quaternion upper, out Quaternion lower));
        (Vector3 newKnee, Vector3 newFoot) = Apply(hip, knee, foot, upper, lower);
        Assert.True(newKnee.Z > 0.05f, $"knee {newKnee} should bend toward the pole");
        Assert.True(newFoot.DistanceTo(new Vector3(0f, 0.3f, 0f)) < 0.002f);
    }

    [Fact]
    public void ADegenerateChainIsRefused()
    {
        Assert.False(FootPlacement.SolveTwoBone(Hip, Hip, Ankle, Vector3.Zero, Vector3.Forward,
            out Quaternion upper, out _));
        Assert.Equal(Quaternion.Identity, upper);
    }
}
