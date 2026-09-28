using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure stamina pacing rules: the regen delay (Phase 29I anti-mash), the post-pause ramp, the
/// low-stamina slowdown, Endurance scaling and the winded latch (movement pass).
/// </summary>
public class StaminaPacingTests
{
    [Fact]
    public void Regen_PausedUntilDelayElapses()
    {
        Assert.False(StaminaPacing.CanRegen(0.0, 0.9f));
        Assert.False(StaminaPacing.CanRegen(0.5, 0.9f));
    }

    [Fact]
    public void Regen_ResumesAtOrPastDelay()
    {
        Assert.True(StaminaPacing.CanRegen(0.9, 0.9f));
        Assert.True(StaminaPacing.CanRegen(2.0, 0.9f));
    }

    [Fact]
    public void Ramp_ZeroDuringPause_ThenRisesToFull()
    {
        Assert.Equal(0f, StaminaPacing.RampFactor(0.5, 0.9f, 0.6f, 0.3f));
        Assert.Equal(0.3f, StaminaPacing.RampFactor(0.9, 0.9f, 0.6f, 0.3f), 4);
        Assert.Equal(0.65f, StaminaPacing.RampFactor(1.2, 0.9f, 0.6f, 0.3f), 4);
        Assert.Equal(1f, StaminaPacing.RampFactor(1.5, 0.9f, 0.6f, 0.3f), 4);
        Assert.Equal(1f, StaminaPacing.RampFactor(9.0, 0.9f, 0.6f, 0.3f), 4);
    }

    [Fact]
    public void Ramp_DisabledSnapsToFull()
    {
        Assert.Equal(1f, StaminaPacing.RampFactor(0.9, 0.9f, 0f, 0.3f));
    }

    [Fact]
    public void LowStamina_SlowsBelowKneeOnly()
    {
        Assert.Equal(1f, StaminaPacing.LowStaminaFactor(0.5f, 0.25f, 0.6f));
        Assert.Equal(1f, StaminaPacing.LowStaminaFactor(0.25f, 0.25f, 0.6f));
        Assert.Equal(0.6f, StaminaPacing.LowStaminaFactor(0f, 0.25f, 0.6f), 4);
        Assert.Equal(0.8f, StaminaPacing.LowStaminaFactor(0.125f, 0.25f, 0.6f), 4);
        Assert.Equal(1f, StaminaPacing.LowStaminaFactor(0f, 0f, 0.6f)); // knee 0 disables it
    }

    [Fact]
    public void Endurance_ScalesAroundBaseline()
    {
        Assert.Equal(1f, StaminaPacing.AttributeFactor(10f, 10f, 0.03f, 0.25f), 4);
        Assert.Equal(1.3f, StaminaPacing.AttributeFactor(20f, 10f, 0.03f, 0.25f), 4);
        Assert.Equal(0.85f, StaminaPacing.AttributeFactor(5f, 10f, 0.03f, 0.25f), 4);
        Assert.Equal(0.25f, StaminaPacing.AttributeFactor(-100f, 10f, 0.03f, 0.25f), 4); // floored
    }

    [Fact]
    public void Winded_LatchesAtZeroAndHoldsUntilThreshold()
    {
        bool winded = StaminaPacing.UpdateWinded(false, 0f, 0.35f);
        Assert.True(winded);
        winded = StaminaPacing.UpdateWinded(winded, 0.2f, 0.35f);
        Assert.True(winded); // hysteresis: a sliver of regen is not recovery
        winded = StaminaPacing.UpdateWinded(winded, 0.35f, 0.35f);
        Assert.False(winded);
        Assert.False(StaminaPacing.UpdateWinded(false, 0.1f, 0.35f)); // low but never emptied
    }
}
