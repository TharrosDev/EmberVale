using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Distance and bearing on the Phase 39.5A map. The eight compass points are the part worth pinning:
/// the rounding has to wrap, and the convention (North = −Z, East = +X) has to match the HUD compass
/// or the map and the strip disagree about which way the player is looking.
/// </summary>
public class MapDistanceTests
{
    [Theory]
    [InlineData(0f, -1f, "map.dir.north")]
    [InlineData(1f, -1f, "map.dir.northeast")]
    [InlineData(1f, 0f, "map.dir.east")]
    [InlineData(1f, 1f, "map.dir.southeast")]
    [InlineData(0f, 1f, "map.dir.south")]
    [InlineData(-1f, 1f, "map.dir.southwest")]
    [InlineData(-1f, 0f, "map.dir.west")]
    [InlineData(-1f, -1f, "map.dir.northwest")]
    public void DirectionKey_NamesAllEightPoints(float dx, float dz, string expected)
    {
        Assert.Equal(expected, MapDistance.DirectionKey(dx, dz));
    }

    [Fact]
    public void DirectionKey_WrapsAroundNorthRatherThanRunningOffTheArray()
    {
        // Just west of due north: rounds to index 8, which must fold back to north.
        Assert.Equal("map.dir.north", MapDistance.DirectionKey(-0.05f, -1f));
        Assert.Equal("map.dir.north", MapDistance.DirectionKey(0.05f, -1f));
    }

    [Fact]
    public void Metres_IsPlanarAndIgnoresNothingElse()
    {
        Assert.Equal(5f, MapDistance.Metres(0f, 0f, 3f, 4f), 3);
        Assert.Equal(0f, MapDistance.Metres(7f, -2f, 7f, -2f), 3);
    }

    [Fact]
    public void Describe_RoundsToWholeMetres()
    {
        (int metres, string dir) = MapDistance.Describe(0f, 0f, 420.4f, 0f);

        Assert.Equal(420, metres);
        Assert.Equal("map.dir.east", dir);
    }

    [Fact]
    public void Describe_NamesTheDirectionOfTheTargetNotTheSource()
    {
        // Target is north-east of the player: +X and -Z.
        (_, string dir) = MapDistance.Describe(10f, 10f, 40f, -20f);
        Assert.Equal("map.dir.northeast", dir);
    }

    [Fact]
    public void Describe_StandingOnItReadsAsHereWithNoDirection()
    {
        (int metres, string dir) = MapDistance.Describe(0f, 0f, 1f, 1f);

        Assert.Equal(0, metres);
        Assert.Equal(string.Empty, dir);
    }

    [Fact]
    public void Describe_JustOutsideHereRadiusGetsADirectionAgain()
    {
        (int metres, string dir) = MapDistance.Describe(0f, 0f, 0f, -(MapDistance.HereRadius + 1f));

        Assert.Equal(7, metres);
        Assert.Equal("map.dir.north", dir);
    }
}
