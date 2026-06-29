using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure charged-cast power curve (Phase 29.5A): 1x at no charge, the cap at full hold, linear
/// between, and clamped past full.
/// </summary>
public class SpellChargeTests
{
    [Fact]
    public void NoCharge_IsOneTimes()
    {
        Assert.Equal(1f, SpellCharge.PowerMultiplier(0f, 1.2f, 2.5f), 3);
    }

    [Fact]
    public void FullAndOverCharge_AreCapped()
    {
        Assert.Equal(2.5f, SpellCharge.PowerMultiplier(1.2f, 1.2f, 2.5f), 3);
        Assert.Equal(2.5f, SpellCharge.PowerMultiplier(5f, 1.2f, 2.5f), 3); // held past full clamps
    }

    [Fact]
    public void HalfCharge_IsHalfwayUpTheCurve()
    {
        // Halfway through the charge time → halfway between 1x and 2.5x = 1.75x.
        Assert.Equal(1.75f, SpellCharge.PowerMultiplier(0.6f, 1.2f, 2.5f), 3);
    }
}
