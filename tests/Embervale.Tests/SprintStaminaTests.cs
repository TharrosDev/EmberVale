using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>The on-foot sprint gate (<see cref="SprintStamina"/>) — MountRules' latch on the rider's legs.</summary>
public class SprintStaminaTests
{
    private const float ResumeAt = 20f;

    [Fact]
    public void WithStaminaASprintIsGranted()
    {
        SprintStamina.Result r = SprintStamina.Step(false, true, 50f, ResumeAt);
        Assert.True(r.Sprinting);
        Assert.False(r.Exhausted);
    }

    [Fact]
    public void NotAskingIsNotSprinting()
    {
        Assert.False(SprintStamina.Step(false, false, 50f, ResumeAt).Sprinting);
    }

    [Fact]
    public void AnEmptyPoolRefusesAndLatches()
    {
        SprintStamina.Result r = SprintStamina.Step(false, true, 0f, ResumeAt);
        Assert.False(r.Sprinting);
        Assert.True(r.Exhausted);
    }

    /// <summary>⚠️ The stutter the latch exists to kill: a sliver of regen must not buy a frame of sprint.</summary>
    [Fact]
    public void ASliverOfRegenDoesNotResumeTheSprint()
    {
        SprintStamina.Result r = SprintStamina.Step(true, true, 3f, ResumeAt);
        Assert.False(r.Sprinting);
        Assert.True(r.Exhausted);
    }

    /// <summary>⚠️ MountRules' second lesson: recovering on the mark alone still sawtooths.</summary>
    [Fact]
    public void RecoveringWhileStillHoldingSprintDoesNotResume()
    {
        SprintStamina.Result r = SprintStamina.Step(true, true, 80f, ResumeAt);
        Assert.False(r.Sprinting);
        Assert.True(r.Exhausted);
    }

    [Fact]
    public void LettingGoAfterRecoveringClearsTheLatch()
    {
        SprintStamina.Result released = SprintStamina.Step(true, false, ResumeAt, ResumeAt);
        Assert.False(released.Exhausted);
        Assert.True(SprintStamina.Step(released.Exhausted, true, ResumeAt, ResumeAt).Sprinting);
    }

    [Fact]
    public void LettingGoBeforeRecoveringDoesNot()
    {
        Assert.True(SprintStamina.Step(true, false, ResumeAt - 1f, ResumeAt).Exhausted);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANonFinitePoolNeverSprints(float stamina)
    {
        SprintStamina.Result r = SprintStamina.Step(false, true, stamina, ResumeAt);
        Assert.False(r.Sprinting);
        Assert.True(r.Exhausted);
    }
}
