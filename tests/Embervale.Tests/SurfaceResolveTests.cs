using Embervale.Player;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins footstep surface resolution and the stride accumulator (Phase 31E) — both pure and load-bearing
/// (a wrong tag map plays the wrong surface; a broken accumulator drops or spams footfalls).
/// </summary>
public class SurfaceResolveTests
{
    [Theory]
    [InlineData("grass", "step.grass")]
    [InlineData("Grass", "step.grass")]
    [InlineData("  WOOD ", "step.wood")]
    [InlineData("plank", "step.wood")]
    [InlineData("snow", "step.snow")]
    [InlineData("stone", "step.stone")]
    [InlineData("concrete", "step.stone")]
    public void CueFromTag_MapsKnownSurfaces(string tag, string expected) =>
        Assert.Equal(expected, Surfaces.CueFromTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lava")]
    public void CueFromTag_UnknownFallsBackToStone(string? tag) =>
        Assert.Equal(Surfaces.DefaultCue, Surfaces.CueFromTag(tag));

    [Fact]
    public void Gait_FiresOncePerStride()
    {
        var gait = new FootstepGait { Stride = 2f };
        Assert.False(gait.Advance(1.0f)); // 1.0 accumulated
        Assert.False(gait.Advance(0.9f)); // 1.9
        Assert.True(gait.Advance(0.3f));  // 2.2 → fire, 0.2 remains
        Assert.False(gait.Advance(1.5f)); // 1.7
        Assert.True(gait.Advance(0.4f));  // 2.1 → fire
    }

    [Fact]
    public void Gait_IgnoresNonPositiveAndReset()
    {
        var gait = new FootstepGait { Stride = 2f };
        Assert.False(gait.Advance(0f));
        Assert.False(gait.Advance(-5f));
        gait.Advance(1.9f);
        gait.Reset();
        Assert.False(gait.Advance(0.2f)); // accumulator was cleared, so 0.2 only
    }
}
