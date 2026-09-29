using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The fading-Weave dial: as potency falls, ordinary magic weakens and costs more (clamped, never
/// useless), corrupted magic strengthens and cheapens. The live value and region wiring are probed.
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
    public void DeadWeave_HitsTheDocumentedFloorsAndCeilings()
    {
        Assert.Equal(WeaveMath.MinOrdinaryPower, WeaveMath.PowerMultiplier(0f, corrupted: false), 3);
        Assert.Equal(WeaveMath.MaxOrdinaryCost, WeaveMath.CostMultiplier(0f, corrupted: false), 3);
        Assert.Equal(WeaveMath.MaxCorruptPower, WeaveMath.PowerMultiplier(0f, corrupted: true), 3);
        Assert.Equal(WeaveMath.MinCorruptCost, WeaveMath.CostMultiplier(0f, corrupted: true), 3);
    }

    [Fact]
    public void OrdinaryMagic_NeverDropsToUselessness_AtAnyPotency()
    {
        for (float p = -1f; p <= 2f; p += 0.05f)
        {
            Assert.InRange(WeaveMath.PowerMultiplier(p, false), WeaveMath.MinOrdinaryPower - 0.0001f, 1.0001f);
            Assert.InRange(WeaveMath.CostMultiplier(p, false), 0.9999f, WeaveMath.MaxOrdinaryCost + 0.0001f);
        }
    }

    [Fact]
    public void CorruptedMagic_IsNeverWorseThanNeutral_AndStaysInBounds()
    {
        for (float p = 0f; p <= 1f; p += 0.05f)
        {
            Assert.InRange(WeaveMath.PowerMultiplier(p, true), 0.9999f, WeaveMath.MaxCorruptPower + 0.0001f);
            Assert.InRange(WeaveMath.CostMultiplier(p, true), WeaveMath.MinCorruptCost - 0.0001f, 1.0001f);
            Assert.True(WeaveMath.PowerMultiplier(p, true) >= WeaveMath.PowerMultiplier(p, false) - 0.0001f);
        }
    }

    [Fact]
    public void FallingPotency_MovesThePathsApart()
    {
        Assert.True(WeaveMath.PowerMultiplier(0.8f, false) > WeaveMath.PowerMultiplier(0.3f, false));
        Assert.True(WeaveMath.PowerMultiplier(0.8f, true) < WeaveMath.PowerMultiplier(0.3f, true));
        Assert.True(WeaveMath.CostMultiplier(0.8f, false) < WeaveMath.CostMultiplier(0.3f, false));
        Assert.True(WeaveMath.CostMultiplier(0.8f, true) > WeaveMath.CostMultiplier(0.3f, true));
    }

    [Theory]
    [InlineData(2f, 1f)]   // above 1 clamps to full
    [InlineData(-1f, 0f)]  // below 0 clamps to dead
    public void Potency_IsClampedToUnitRange(float input, float equivalent)
    {
        Assert.Equal(WeaveMath.PowerMultiplier(equivalent, false), WeaveMath.PowerMultiplier(input, false), 3);
    }

    [Fact]
    public void NaNPotency_ReadsAsFull_NotAsPoison()
    {
        Assert.Equal(1f, WeaveMath.PowerMultiplier(float.NaN, false), 3);
        Assert.Equal(WeaveBand.Strong, WeaveMath.BandOf(float.NaN));
    }

    [Theory]
    [InlineData(1.0f, WeaveBand.Strong)]   // Ember Crown
    [InlineData(0.9f, WeaveBand.Strong)]
    [InlineData(0.8f, WeaveBand.Thinning)] // Sunspire
    [InlineData(0.5f, WeaveBand.Frayed)]   // Frostfang
    [InlineData(0.4f, WeaveBand.Frayed)]   // Ashen Wilds
    [InlineData(0.35f, WeaveBand.Failing)] // Pale Concord
    [InlineData(0.3f, WeaveBand.Failing)]  // Celestial Realm
    [InlineData(0.0f, WeaveBand.Failing)]
    public void Bands_MatchTheShippedRegions(float potency, WeaveBand expected)
    {
        Assert.Equal(expected, WeaveMath.BandOf(potency));
    }

    [Fact]
    public void Indicator_HidesOnlyWhileStrong_AndPrintsSignedPercent()
    {
        Assert.False(WeaveMath.ShowsIndicator(1f));
        Assert.True(WeaveMath.ShowsIndicator(0.8f));
        Assert.Equal(-28, WeaveMath.PowerPercent(0.3f, corrupted: false));
        Assert.Equal(11, WeaveMath.PowerPercent(0.7f, corrupted: true));
        Assert.Equal(0, WeaveMath.PowerPercent(1f, corrupted: true));
    }
}
