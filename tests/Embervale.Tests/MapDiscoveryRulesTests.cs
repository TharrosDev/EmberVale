using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="MapDiscoveryRules"/> (2026-09 world rebuild): a town is discovered by seeing it,
/// a shop by walking up to it, and a hill between the player and a town hides it.
/// </summary>
public class MapDiscoveryRulesTests
{
    [Fact]
    public void TiersDefaultToTheirSightRadiusAndAnAuthoredRadiusWins()
    {
        Assert.Equal(MapDiscoveryRules.PrimarySightRadius, MapDiscoveryRules.RadiusFor(MapTier.Primary, 0f));
        Assert.Equal(MapDiscoveryRules.SecondarySightRadius, MapDiscoveryRules.RadiusFor(MapTier.Secondary, 0f));
        Assert.Equal(MapDiscoveryRules.WalkUpRadius, MapDiscoveryRules.RadiusFor(MapTier.Detail, 0f));
        Assert.Equal(300f, MapDiscoveryRules.RadiusFor(MapTier.Detail, 300f));
    }

    [Fact]
    public void OnlyBeyondWalkUpRangeDoesTheLandNeedToAllowAView()
    {
        Assert.False(MapDiscoveryRules.NeedsLineOfSight(MapDiscoveryRules.WalkUpRadius));
        Assert.True(MapDiscoveryRules.NeedsLineOfSight(MapDiscoveryRules.SecondarySightRadius));
    }

    [Fact]
    public void FlatCountryShowsATownAcrossIt()
    {
        Assert.True(MapDiscoveryRules.HasLineOfSight((0f, 1.7f, 0f), (150f, 8f, 0f), (_, _) => 0f));
    }

    [Fact]
    public void ARiseBetweenThePlayerAndTheTownHidesIt()
    {
        // A 20 m ridge halfway there.
        Assert.False(MapDiscoveryRules.HasLineOfSight(
            (0f, 1.7f, 0f), (150f, 8f, 0f), (x, _) => x is > 60f and < 90f ? 20f : 0f));
    }

    [Fact]
    public void ThePlacesOwnHillDoesNotHideIt()
    {
        // Ground rising inside the last few metres is the town's own knoll, not an obstruction.
        Assert.True(MapDiscoveryRules.HasLineOfSight(
            (0f, 1.7f, 0f), (150f, 8f, 0f), (x, _) => x > 145f ? 30f : 0f));
    }
}
