using Embervale.Combat.Actions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The one authoritative action timeline (the 2026-09-04 combat/animation overhaul).
///
/// These are the rules that used to live inside a <c>double</c> stopwatch in
/// <c>MeleeWeaponComponent</c> while the visible clip ran on an entirely separate clock. Pulling
/// them into pure arithmetic is what makes "the hit landed before the sword moved" a testable claim
/// instead of a thing you have to watch for.
/// </summary>
public class ActionTimelineTests
{
    private static readonly ActionWindows Sword = new(
        ActiveFrom: 0.27f, ActiveTo: 0.49f, CancelFrom: 0.49f, ComboFrom: 0.49f, ComboTo: 1f);

    [Theory]
    [InlineData(0f, ActionPhase.Startup)]
    [InlineData(0.26f, ActionPhase.Startup)]
    [InlineData(0.27f, ActionPhase.Active)]
    [InlineData(0.48f, ActionPhase.Active)]
    [InlineData(0.49f, ActionPhase.Recovery)]
    [InlineData(0.99f, ActionPhase.Recovery)]
    [InlineData(1f, ActionPhase.Idle)]
    public void ThePhaseFollowsTheAuthoredFractions(float progress, ActionPhase expected) =>
        Assert.Equal(expected, ActionTimeline.PhaseAt(progress, Sword));

    [Fact]
    public void TheHitWindowIsOpenOnlyBetweenActiveFromAndActiveTo()
    {
        Assert.False(ActionTimeline.IsActive(0.26f, Sword));
        Assert.True(ActionTimeline.IsActive(0.27f, Sword));
        Assert.True(ActionTimeline.IsActive(0.489f, Sword));
        Assert.False(ActionTimeline.IsActive(0.49f, Sword));
    }

    [Fact]
    public void AFinishedActionIsNeverActive()
    {
        // The guard matters because a clip that overruns its own length reports progress >= 1, and
        // an "active" test written only as a range would leave the hitbox open past the swing.
        var alwaysOn = new ActionWindows(0f, 1f, 1f, 1f, 1f);
        Assert.False(ActionTimeline.IsActive(1f, alwaysOn));
        Assert.False(ActionTimeline.IsActive(1.4f, alwaysOn));
    }

    [Fact]
    public void CommitmentEndsExactlyAtTheCancelWindow()
    {
        Assert.False(ActionTimeline.CanCancel(0.48f, Sword));
        Assert.True(ActionTimeline.CanCancel(0.49f, Sword));
    }

    [Fact]
    public void TheComboWindowIsInclusiveAtBothEnds()
    {
        Assert.False(ActionTimeline.InComboWindow(0.48f, Sword));
        Assert.True(ActionTimeline.InComboWindow(0.49f, Sword));
        Assert.True(ActionTimeline.InComboWindow(1f, Sword));
    }

    [Fact]
    public void AStaggerCancelsAStartupButNeverALiveBlow()
    {
        // 36C, carried across verbatim: once the hitbox is open the attack is committed, which is
        // what keeps the punish window something to aim for rather than a race.
        Assert.True(ActionTimeline.StaggerCancels(0.1f, Sword, interruptible: true));
        Assert.False(ActionTimeline.StaggerCancels(0.3f, Sword, interruptible: true));
        Assert.False(ActionTimeline.StaggerCancels(0.6f, Sword, interruptible: true));
    }

    [Fact]
    public void HyperarmorRefusesToBeCancelledAtAll() =>
        Assert.False(ActionTimeline.StaggerCancels(0.1f, Sword, interruptible: false));

    [Fact]
    public void AClipIsWarpedToSpanExactlyTheActionsDuration()
    {
        // The whole point: a 1.2 s Sword_Slash driving a 0.55 s action plays at 2.18x, so the Iron
        // King's heave and a dagger's flick stop being the same clip at the same speed.
        Assert.Equal(2f, ActionTimeline.ClipSpeedFor(clipSeconds: 1.2f, actionSeconds: 0.6f), 3);
        Assert.Equal(0.5f, ActionTimeline.ClipSpeedFor(clipSeconds: 0.6f, actionSeconds: 1.2f), 3);
    }

    [Fact]
    public void AClipSpeedIsClampedSoAPathologicalPairingStillReadsAsASwing()
    {
        // A 4 s clip forced into a 0.05 s action would otherwise play at 80x — one flickering frame.
        Assert.Equal(12f, ActionTimeline.ClipSpeedFor(4f, 0.05f), 3);
        Assert.Equal(0.1f, ActionTimeline.ClipSpeedFor(0.05f, 4f), 3);
    }

    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(-1f, 1f, 1f)]
    public void AZeroLengthClipCannotDivideByZero(float clip, float action, float expected) =>
        Assert.Equal(expected, ActionTimeline.ClipSpeedFor(clip, action), 3);

    [Fact]
    public void TheFallbackProgressIsTheSameNumberTheClipWouldHaveGiven()
    {
        // An actor with no clip for the slot runs the identical fractions off elapsed/duration, so
        // a body without animation fights correctly rather than not at all.
        Assert.Equal(0f, ActionTimeline.ProgressOf(0d, 0.6d), 3);
        Assert.Equal(0.5f, ActionTimeline.ProgressOf(0.3d, 0.6d), 3);
        Assert.Equal(1f, ActionTimeline.ProgressOf(0.9d, 0.6d), 3);
    }

    [Fact]
    public void AZeroDurationActionIsAlreadyOverRatherThanRunningForever() =>
        Assert.Equal(1f, ActionTimeline.ProgressOf(0d, 0d), 3);

    [Fact]
    public void TheDefaultWindowShapeIsOrdered()
    {
        // A window set whose fractions cross produces an action that is active before it starts or
        // cancellable before it lands. The default is the shape everything unauthored inherits.
        ActionWindows d = ActionWindows.Default;
        Assert.True(d.ActiveFrom < d.ActiveTo);
        Assert.True(d.ActiveTo <= d.CancelFrom);
        Assert.True(d.ComboFrom <= d.ComboTo);
        Assert.True(d.ComboTo <= 1f);
    }
}
