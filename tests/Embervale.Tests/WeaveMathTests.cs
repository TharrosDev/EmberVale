using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The fading-Weave dial (Phase 29.5E): how region magic potency bends cast power and cost. As potency
/// falls, ordinary magic weakens and costs more while corrupted magic does the opposite — the dying-world
/// temptation made mechanical. The live value + region wiring is Godot-bound and verified by build/run.
/// </summary>
public class WeaveMathTests
{
    [Fact]
    public void FullPotency_IsNeutral_ForBothPaths()
    {
        Assert.Equal(1f, WeaveMath.PowerMultiplier(1f, corrupted: false), 3);
        Assert.Equal(1f, WeaveMath.CostMultiplier(1f, corrupted: false), 3);
        Assert.Equal(1f, WeaveMath.PowerMultiplier(1f, corrupted: true), 3);
        Assert.Equal(1f, WeaveMath.CostMultiplier(1f, corrupted: true), 3);
    }

    [Fact]
    public void DeadWeave_WeakensAndTaxesOrdinaryMagic()
    {
        Assert.Equal(0.5f, WeaveMath.PowerMultiplier(0f, corrupted: false), 3);
        Assert.Equal(1.5f, WeaveMath.CostMultiplier(0f, corrupted: false), 3);
    }

    [Fact]
    public void DeadWeave_EmpowersAndCheapensCorruptedMagic()
    {
        Assert.Equal(1.4f, WeaveMath.PowerMultiplier(0f, corrupted: true), 3);
        Assert.Equal(0.6f, WeaveMath.CostMultiplier(0f, corrupted: true), 3);
    }

    [Fact]
    public void FallingPotency_MovesThePathsApart()
    {
        // Ordinary magic strengthens with potency; corrupted magic weakens with it. The two cross at 1.
        Assert.True(WeaveMath.PowerMultiplier(0.8f, false) > WeaveMath.PowerMultiplier(0.3f, false));
        Assert.True(WeaveMath.PowerMultiplier(0.8f, true) < WeaveMath.PowerMultiplier(0.3f, true));
    }

    [Theory]
    [InlineData(2f, 1f)]   // above 1 clamps to full
    [InlineData(-1f, 0f)]  // below 0 clamps to dead
    public void Potency_IsClampedToUnitRange(float input, float equivalent)
    {
        Assert.Equal(WeaveMath.PowerMultiplier(equivalent, false), WeaveMath.PowerMultiplier(input, false), 3);
    }
}
