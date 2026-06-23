using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure load/unload rule behind the Phase 25B `RegionStreamer`. The streaming itself
/// (instancing, the per-frame budget, the player distance) runs in-engine, but the
/// load/keep/unload decision — including the hysteresis that stops boundary thrash — is pure and
/// load-bearing, so it is pinned here.
/// </summary>
public class StreamDecisionTests
{
    private const float LoadRadius = 30f;
    private const float Margin = 10f;

    [Fact]
    public void InsideRadius_NotLoaded_Loads()
    {
        Assert.Equal(StreamAction.Load, StreamDecision.Decide(20f, LoadRadius, Margin, isLoaded: false));
    }

    [Fact]
    public void OutsideRadius_NotLoaded_Keeps()
    {
        Assert.Equal(StreamAction.Keep, StreamDecision.Decide(45f, LoadRadius, Margin, isLoaded: false));
    }

    [Fact]
    public void JustOutsideRadius_Loaded_Keeps_Hysteresis()
    {
        // Between LoadRadius (30) and LoadRadius+margin (40): a loaded cell stays loaded.
        Assert.Equal(StreamAction.Keep, StreamDecision.Decide(35f, LoadRadius, Margin, isLoaded: true));
    }

    [Fact]
    public void BeyondMargin_Loaded_Unloads()
    {
        Assert.Equal(StreamAction.Unload, StreamDecision.Decide(41f, LoadRadius, Margin, isLoaded: true));
    }

    [Fact]
    public void InsideRadius_Loaded_Keeps()
    {
        Assert.Equal(StreamAction.Keep, StreamDecision.Decide(10f, LoadRadius, Margin, isLoaded: true));
    }

    [Fact]
    public void ExactlyAtRadius_NotLoaded_Loads()
    {
        Assert.Equal(StreamAction.Load, StreamDecision.Decide(LoadRadius, LoadRadius, Margin, isLoaded: false));
    }
}
