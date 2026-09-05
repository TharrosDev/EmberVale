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
}
