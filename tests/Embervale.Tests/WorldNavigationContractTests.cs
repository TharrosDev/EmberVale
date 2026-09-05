using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldNavigationContractTests
{
    [Fact]
    public void RepresentativeProfilesUseThePreparedWorldLayer()
    {
        foreach (WorldNavigationAgentProfile profile in WorldNavigationContract.Profiles)
        {
            Assert.Equal(1u, profile.NavigationLayer);
        }
    }

    [Fact]
    public void NarrowRouteSupportsGoblinButNotLargeEnemy()
    {
        Assert.True(WorldNavigationContract.RouteSupports(
            1.2f, 0.5f, WorldNavigationContract.Profiles[2]));
        Assert.False(WorldNavigationContract.RouteSupports(
            1.2f, 0.5f, WorldNavigationContract.Profiles[3]));
    }
}
