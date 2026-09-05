using Embervale.Combat;
using Embervale.Player;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Camera profiles and lock-on scoring — the two places the camera decides something on the player's
/// behalf, and therefore the two places it can decide wrong.
/// </summary>
public class CameraProfileTests
{
    [Fact]
    public void ThePriorityOrderIsMostSpecificFirst()
    {
        // Aiming beats a lock, a lock beats generic combat, combat beats sprinting — the order in
        // which each matters to what the player is trying to see. A different order would frame a
        // locked-on duel as a sprint the moment the player ran at their target.
        Assert.Equal(CameraContext.Aim,
            CameraProfile.Resolve(aiming: true, lockedOn: true, inCombat: true, sprinting: true));
        Assert.Equal(CameraContext.TargetLock,
            CameraProfile.Resolve(aiming: false, lockedOn: true, inCombat: true, sprinting: true));
        Assert.Equal(CameraContext.Combat,
            CameraProfile.Resolve(aiming: false, lockedOn: false, inCombat: true, sprinting: true));
        Assert.Equal(CameraContext.Sprint,
            CameraProfile.Resolve(aiming: false, lockedOn: false, inCombat: false, sprinting: true));
        Assert.Equal(CameraContext.Exploration,
            CameraProfile.Resolve(aiming: false, lockedOn: false, inCombat: false, sprinting: false));
    }

    [Fact]
    public void ExplorationChangesNothing()
    {
        // ⚠️ The neutral profile has to be exactly neutral. Anything else means the player's own
        // distance and FOV sliders never actually apply while simply walking around.
        CameraProfile p = CameraProfile.For(CameraContext.Exploration);
        Assert.Equal(1f, p.DistanceScale, 4);
        Assert.Equal(0f, p.RiseOffset, 4);
        Assert.Equal(0f, p.FovOffset, 4);
        Assert.Equal(1f, p.ShoulderScale, 4);
    }

    [Fact]
    public void SprintPullsBackAndWidensAndAimPullsInAndNarrows()
    {
        CameraProfile sprint = CameraProfile.For(CameraContext.Sprint);
        Assert.True(sprint.DistanceScale > 1f, "sprint should pull the camera back");
        Assert.True(sprint.FovOffset > 0f, "sprint should widen the view");

        CameraProfile aim = CameraProfile.For(CameraContext.Aim);
        Assert.True(aim.DistanceScale < 1f, "aiming should bring the camera in");
        Assert.True(aim.FovOffset < 0f, "aiming should narrow the view");
    }

    [Fact]
    public void EveryProfileStaysWithinReasonableBounds()
    {
        // A profile is a lean, not a replacement. Halving or doubling the player's chosen distance
        // is already a lot; anything past that is a different camera rather than the same one framed
        // differently.
        foreach (CameraContext context in System.Enum.GetValues<CameraContext>())
        {
            CameraProfile p = CameraProfile.For(context);
            Assert.InRange(p.DistanceScale, 0.5f, 1.5f);
            Assert.InRange(p.FovOffset, -20f, 20f);
            Assert.InRange(p.ShoulderScale, 0.5f, 2f);
            Assert.True(p.BlendSeconds > 0f, $"{context} must ease rather than cut");
        }
    }

    [Fact]
    public void BlendingMovesEveryFieldAndEndsExactlyOnTheTarget()
    {
        CameraProfile a = CameraProfile.For(CameraContext.Exploration);
        CameraProfile b = CameraProfile.For(CameraContext.Aim);

        CameraProfile half = CameraProfile.Blend(a, b, 0.5f);
        Assert.InRange(half.DistanceScale, b.DistanceScale, a.DistanceScale);
        Assert.InRange(half.FovOffset, b.FovOffset, a.FovOffset);

        Assert.Equal(b, CameraProfile.Blend(a, b, 1f));
        Assert.Equal(a, CameraProfile.Blend(a, b, 0f));
    }

    [Fact]
    public void WhatThePlayerIsLookingAtBeatsWhatTheyAreStandingNear()
    {
        // ⚠️ THE DEFECT THIS FIXES. Selection used to be distance alone, so an enemy behind the
        // player beat one they were aiming straight at. Angle is weighted three times as heavily.
        float aheadFar = LockOn.Score(
            distance: 14f, angleFromView: 0.05f, maxDistance: 18f, maxAngle: 1.3f, hasLineOfSight: true);
        float besideNear = LockOn.Score(
            distance: 3f, angleFromView: 1.2f, maxDistance: 18f, maxAngle: 1.3f, hasLineOfSight: true);

        Assert.True(aheadFar < besideNear,
            $"the target being aimed at scored {aheadFar}, worse than one off to the side at {besideNear}");
    }

    [Fact]
    public void NothingOutOfRangeBehindOrBehindCoverIsACandidate()
    {
        Assert.True(LockOn.Score(30f, 0.1f, 18f, 1.3f, true) < 0f, "out of range");
        Assert.True(LockOn.Score(5f, 2.5f, 18f, 1.3f, true) < 0f, "behind the player");
        Assert.True(LockOn.Score(5f, 0.1f, 18f, 1.3f, false) < 0f, "behind cover");
    }

    [Fact]
    public void CloserAndMoreCentredScoresBetter()
    {
        float centred = LockOn.Score(5f, 0.05f, 18f, 1.3f, true);
        float offCentre = LockOn.Score(5f, 0.6f, 18f, 1.3f, true);
        float far = LockOn.Score(15f, 0.05f, 18f, 1.3f, true);

        Assert.True(centred < offCentre);
        Assert.True(centred < far);
    }

    [Fact]
    public void SwitchingNeedsAMeaningfulImprovement()
    {
        // ⚠️ Hysteresis is the difference between a lock and a flicker. Two enemies scoring within a
        // hair of each other would otherwise trade places every frame the player's aim drifts.
        Assert.False(LockOn.ShouldSwitch(currentScore: 1.0f, challengerScore: 0.99f, margin: 0.2f));
        Assert.True(LockOn.ShouldSwitch(currentScore: 1.0f, challengerScore: 0.5f, margin: 0.2f));
    }

    [Fact]
    public void AnythingBeatsHavingNoLockAndNothingBeatsAnInvalidChallenger()
    {
        Assert.True(LockOn.ShouldSwitch(currentScore: -1f, challengerScore: 5f, margin: 0.2f));
        Assert.False(LockOn.ShouldSwitch(currentScore: 1f, challengerScore: -1f, margin: 0.2f));
    }
}
