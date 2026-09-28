using System;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the rules behind the camera's event-driven special views: which one wins, how far
/// each may lean, when the player takes the camera back, and that nothing leaves a residue.</summary>
public class SpecialViewMathTests
{
    private static readonly SpecialView[] Recipes =
    {
        SpecialView.Dialogue, SpecialView.BossIntro, SpecialView.Death, SpecialView.Focus,
    };

    [Fact]
    public void Choose_RanksDeathOverBossOverDialogueOverFocus()
    {
        Assert.Equal(SpecialView.Death, SpecialViewMath.Choose(true, true, true, true));
        Assert.Equal(SpecialView.BossIntro, SpecialViewMath.Choose(false, true, true, true));
        Assert.Equal(SpecialView.Dialogue, SpecialViewMath.Choose(false, false, true, true));
        Assert.Equal(SpecialView.Focus, SpecialViewMath.Choose(false, false, false, true));
        Assert.Equal(SpecialView.None, SpecialViewMath.Choose(false, false, false, false));
    }

    [Fact]
    public void Advance_CrossesTheRangeInTheGivenSecondsAndClamps()
    {
        float up = 0f;
        for (int i = 0; i < 10; i++)
        {
            up = SpecialViewMath.Advance(up, true, 0.1f, 1f, 2f);
        }

        Assert.Equal(1f, up, 4);
        Assert.Equal(1f, SpecialViewMath.Advance(up, true, 5f, 1f, 2f)); // never overshoots

        // Going out takes the (slower) out time: half a second is a quarter of two seconds.
        Assert.Equal(0.75f, SpecialViewMath.Advance(1f, false, 0.5f, 1f, 2f), 4);
        Assert.Equal(0f, SpecialViewMath.Advance(0.1f, false, 5f, 1f, 2f));
    }

    [Fact]
    public void Advance_NonPositiveDurationSnapsAndNegativeDtIsIgnored()
    {
        Assert.Equal(1f, SpecialViewMath.Advance(0f, true, 0.016f, 0f, 1f));
        Assert.Equal(0f, SpecialViewMath.Advance(1f, false, 0.016f, 1f, 0f));
        Assert.Equal(0.5f, SpecialViewMath.Advance(0.5f, true, -1f, 1f, 1f));
    }

    [Fact]
    public void Ease_IsAnchoredAtBothEnds()
    {
        Assert.Equal(0f, SpecialViewMath.Ease(-1f));
        Assert.Equal(0f, SpecialViewMath.Ease(0f));
        Assert.Equal(0.5f, SpecialViewMath.Ease(0.5f), 5);
        Assert.Equal(1f, SpecialViewMath.Ease(2f));
    }

    [Fact]
    public void Recipes_AreRestrainedAndNeutralWhenOff()
    {
        foreach (SpecialView view in Recipes)
        {
            ViewRecipe r = ViewRecipe.For(view);
            Assert.Equal(1f, r.Distance(0f));                         // off = no change at all
            Assert.Equal(r.DistanceScale, r.Distance(1f), 5);
            Assert.InRange(r.DistanceScale, 0.8f, 1.4f);              // never a lurch
            Assert.InRange(r.FovDegrees, -6f, 0f);                    // narrows, never widens
            Assert.True(r.MaxYaw <= SpecialViewMath.Rad(15f));        // the crosshair stays near where aimed
            Assert.True(r.MaxPitch <= SpecialViewMath.Rad(9f));
            Assert.True(r.InSeconds >= 0.5f && r.OutSeconds >= 0.5f); // eased, never a cut
        }
    }

    [Fact]
    public void Recipes_HaveTheirCharacter()
    {
        Assert.True(ViewRecipe.Dialogue.DistanceScale < 1f);          // pushes in
        Assert.True(ViewRecipe.Death.DistanceScale > 1f);             // pulls back
        Assert.True(ViewRecipe.Death.DropMetres < 0f);                // and sinks
        Assert.Equal(0f, ViewRecipe.Death.MaxYaw);                    // a fall is never steered at anything
        Assert.Equal(1f, ViewRecipe.Focus.DistanceScale);             // focus leans, never dollies
        Assert.Equal(0f, ViewRecipe.Focus.FovDegrees);
        Assert.True(ViewRecipe.BossIntro.MaxYaw < ViewRecipe.Dialogue.MaxYaw); // held and watching, but still modest
        Assert.True(ViewRecipe.Focus.MaxYaw < ViewRecipe.Dialogue.MaxYaw);
        Assert.Equal(0.5f, ViewRecipe.Dialogue.Advance(0f, true, ViewRecipe.Dialogue.InSeconds * 0.5f), 4);
    }

    [Fact]
    public void Recipes_NoneIsNeutral()
    {
        ViewRecipe none = ViewRecipe.For(SpecialView.None);
        Assert.Equal(1f, none.DistanceScale);
        Assert.Equal(0f, none.FovDegrees);
        Assert.Equal(0f, none.MaxYaw);
    }

    [Fact]
    public void BossFramingApplies_OnlyToABossThatIsNear()
    {
        Assert.True(SpecialViewMath.BossFramingApplies(12f));
        Assert.True(SpecialViewMath.BossFramingApplies(SpecialViewMath.BossFramingRange));
        Assert.False(SpecialViewMath.BossFramingApplies(SpecialViewMath.BossFramingRange + 1f));
        Assert.False(SpecialViewMath.BossFramingApplies(-1f));
    }

    [Theory]
    [InlineData(0f, 1.5f)]
    [InlineData(2.5f, 2.5f)]
    [InlineData(30f, 4f)]
    public void BossHoldSeconds_FollowsTheIntroLockInsideASaneWindow(float introLock, float expected)
    {
        Assert.Equal(expected, SpecialViewMath.BossHoldSeconds(introLock));
    }

    [Fact]
    public void DeathHolding_IsTrueOnlyInsideTheHold()
    {
        Assert.False(SpecialViewMath.DeathHolding(-1f));              // never died
        Assert.True(SpecialViewMath.DeathHolding(0f));
        Assert.True(SpecialViewMath.DeathHolding(SpecialViewMath.DeathHoldSeconds - 0.01f));
        Assert.False(SpecialViewMath.DeathHolding(SpecialViewMath.DeathHoldSeconds));
    }

    [Fact]
    public void FocusLeanApplies_NeedsExplorationAndSomethingBigEnough()
    {
        Vector3 shrine = new(1f, 2f, 1f);
        Assert.True(SpecialViewMath.FocusLeanApplies(CameraContext.Exploration, shrine));
        Assert.False(SpecialViewMath.FocusLeanApplies(CameraContext.Combat, shrine));
        Assert.False(SpecialViewMath.FocusLeanApplies(CameraContext.TargetLock, shrine));
        Assert.False(SpecialViewMath.FocusLeanApplies(CameraContext.Aim, shrine));
        Assert.False(SpecialViewMath.FocusLeanApplies(CameraContext.Sprint, shrine));
        Assert.False(SpecialViewMath.FocusLeanApplies(CameraContext.Exploration, new Vector3(0.3f, 0.2f, 0.3f))); // loot
    }

    [Fact]
    public void MountedDistance_IsNeutralOnFootAndWiderInTheSaddle()
    {
        Assert.Equal(1f, SpecialViewMath.MountedDistance(0f));
        Assert.Equal(SpecialViewMath.MountedDistanceScale, SpecialViewMath.MountedDistance(1f), 5);
        Assert.InRange(SpecialViewMath.MountedDistanceScale, 1.05f, 1.3f);
        Assert.InRange(SpecialViewMath.MountedDistance(0.5f), 1f, SpecialViewMath.MountedDistanceScale);
    }

    [Fact]
    public void LookPoint_SitsOnTheCentreLineAndLeansTowardTheTop()
    {
        Vector3 min = new(-1f, 0f, -1f);
        Vector3 max = new(1f, 2f, 3f);

        Assert.Equal(new Vector3(0f, 1f, 1f), SpecialViewMath.LookPoint(min, max, 0f));

        Vector3 head = SpecialViewMath.LookPoint(min, max, 0.6f);
        Assert.Equal(1.6f, head.Y, 5);
        Assert.Equal(0f, head.X, 5);

        Assert.Equal(2f, SpecialViewMath.LookPoint(min, max, 9f).Y, 5); // clamped to the top
    }

    [Fact]
    public void LookAngles_ForwardIsZeroAndSignsMatchTheCameraConvention()
    {
        float max = SpecialViewMath.Rad(20f);

        Assert.Equal(Vector2.Zero, SpecialViewMath.LookAngles(Vector3.Zero, max, max));
        Vector2 ahead = SpecialViewMath.LookAngles(new Vector3(0f, 0f, -5f), max, max);
        Assert.Equal(0f, ahead.X, 5);
        Assert.Equal(0f, ahead.Y, 5);

        // Subject to the right (+X) needs a negative yaw; above (+Y) a positive pitch.
        Assert.True(SpecialViewMath.LookAngles(new Vector3(1f, 0f, -5f), max, max).Y < 0f);
        Assert.True(SpecialViewMath.LookAngles(new Vector3(0f, 1f, -5f), max, max).X > 0f);
    }

    [Fact]
    public void LookAngles_FollowsSmallOffsetsAndNeverExceedsTheLimit()
    {
        float max = SpecialViewMath.Rad(14f);

        // About 1.1 degrees to the left: a small offset is followed almost exactly.
        float yaw = SpecialViewMath.LookAngles(new Vector3(-0.1f, 0f, -5f), max, max).Y;
        Assert.Equal(MathF.Atan2(0.1f, 5f), yaw, 3);

        Vector2 hard = SpecialViewMath.LookAngles(new Vector3(-5f, 4f, -0.2f), max, max);
        Assert.InRange(hard.Y, 0f, max);
        Assert.InRange(hard.X, 0f, max);
    }

    [Fact]
    public void LookAngles_FadesOutForASubjectBehindTheCamera()
    {
        float max = SpecialViewMath.Rad(14f);
        Vector2 behind = SpecialViewMath.LookAngles(new Vector3(0.2f, 0f, 5f), max, max);
        Assert.Equal(0f, behind.Y, 4);
        Assert.Equal(0f, behind.X, 4);

        // Halfway through the fade band it is neither full nor gone.
        float mid = (SpecialViewMath.BehindFadeStart + SpecialViewMath.BehindFadeEnd) * 0.5f;
        Vector3 edge = new(-MathF.Sin(mid), 0f, -MathF.Cos(mid));
        Assert.InRange(SpecialViewMath.LookAngles(edge, max, max).Y, 0.01f, max * 0.99f);
    }

    [Fact]
    public void SoftLimit_IsIdentityNearZeroAndBoundedAtTheLimit()
    {
        Assert.Equal(0.01f, SpecialViewMath.SoftLimit(0.01f, 0.5f), 3);
        Assert.InRange(SpecialViewMath.SoftLimit(50f, 0.5f), 0.499f, 0.5f);
        Assert.Equal(-SpecialViewMath.SoftLimit(0.4f, 0.5f), SpecialViewMath.SoftLimit(-0.4f, 0.5f), 6);
        Assert.Equal(0f, SpecialViewMath.SoftLimit(1f, 0f));
    }

    [Fact]
    public void LookMagnitude_TakesTheLargerOfMouseAndStick()
    {
        Assert.Equal(0f, SpecialViewMath.LookMagnitude(0f, 0f));
        Assert.Equal(1f, SpecialViewMath.LookMagnitude(400f, 0f));    // a flick saturates
        Assert.Equal(0.6f, SpecialViewMath.LookMagnitude(0f, 0.6f), 5);
        Assert.Equal(0.5f, SpecialViewMath.LookMagnitude(8f, 0.3f), 5);
    }

    [Fact]
    public void IsSteering_IgnoresTremorAndCatchesRealInput()
    {
        Assert.False(SpecialViewMath.IsSteering(SpecialViewMath.LookMagnitude(2f, 0.1f)));
        Assert.True(SpecialViewMath.IsSteering(SpecialViewMath.LookMagnitude(6f, 0f)));
        Assert.True(SpecialViewMath.IsSteering(SpecialViewMath.LookMagnitude(0f, 0.4f)));
    }

    [Fact]
    public void LookYield_SteeringTakesTheLeanBackAtOnce()
    {
        var y = new LookYield();
        Assert.Equal(1f, y.Value);
        y.Update(0.016f, steering: true, holdSeconds: 0.8f, recoverSeconds: 2f);
        Assert.Equal(0f, y.Value);
    }

    [Fact]
    public void LookYield_RecoversOnlyAfterTheHoldAndOnlyOverTheRecoverTime()
    {
        var y = new LookYield();
        y.Update(0.016f, true, 0.8f, 2f);

        y.Update(0.5f, false, 0.8f, 2f);                              // still inside the hold
        Assert.Equal(0f, y.Value);

        y.Update(0.5f, false, 0.8f, 2f);                              // hold passed, one recover step
        Assert.InRange(y.Value, 0.2f, 0.3f);

        for (int i = 0; i < 20; i++)
        {
            y.Update(0.5f, false, 0.8f, 2f);
        }

        Assert.Equal(1f, y.Value);
    }

    [Fact]
    public void LookYield_ANewSteerRestartsTheHold()
    {
        var y = new LookYield();
        y.Update(0.016f, true, 0.5f, 1f);
        y.Update(0.6f, false, 0.5f, 1f);
        Assert.True(y.Value > 0f);

        y.Update(0.016f, true, 0.5f, 1f);
        Assert.Equal(0f, y.Value);
        y.Update(0.3f, false, 0.5f, 1f);
        Assert.Equal(0f, y.Value);
    }

    [Fact]
    public void LookYield_ZeroRecoverLatchesUntilReset()
    {
        var y = new LookYield();
        y.Update(0.016f, true, 0f, 0f);
        for (int i = 0; i < 100; i++)
        {
            y.Update(1f, false, 0f, 0f);
        }

        Assert.Equal(0f, y.Value);

        y.Reset();
        Assert.Equal(1f, y.Value);
    }

    [Fact]
    public void EveryViewAtOnce_StillCombinesIntoASaneNudge()
    {
        // Every recipe at full strength at once is the worst case the rig could be handed.
        CameraNudge all = CameraNudge.Identity;
        foreach (SpecialView view in Recipes)
        {
            ViewRecipe r = ViewRecipe.For(view);
            all = all.Combine(new CameraNudge(
                new Vector3(0f, r.DropMetres, 0f), Vector3.Zero, r.FovDegrees, r.DistanceScale));
        }

        Assert.InRange(all.DistanceScale, 0.9f, 1.6f);
        Assert.InRange(all.FovOffset, -15f, 0f);
    }
}
