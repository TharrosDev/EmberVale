using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure stamina regen-delay rule (Phase 29I anti-mash): regen stays paused until enough idle
/// time has passed since the last spend.
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
}
