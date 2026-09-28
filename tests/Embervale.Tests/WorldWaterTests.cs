using System.Collections.Generic;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The non-swimming water contract. The one thing worth pinning here is that a body drawn LARGER
/// than its basin costs nothing where it overhangs the bank — that is the property the whole
/// "author it generously, the terrain draws the shoreline" rule rests on.
/// </summary>
public sealed class WorldWaterTests
{
    private static WorldHeightfield Bowl(float depth) =>
        new(7, 0f, 1f, new List<WorldTerrainMath.Landform>
        {
            new(WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f,
                20f, 20f, 0f, depth, 0.6f, 1f),
        });

    private static readonly List<WorldWater.Body> Pool = new() { new(0f, 0f, 40f, 40f, 0f) };

    [Fact]
    public void DepthIsTheGapBetweenTheSurfaceAndTheGround()
    {
        WorldWater.Set(Pool);
        Assert.Equal(6f, WorldWater.DepthAt(0f, 0f, Bowl(-6f)), 2);
    }

    [Fact]
    public void GroundAboveTheWaterlineInsideTheRectangleIsDry()
    {
        WorldWater.Set(Pool);
        // 35 m out: inside the declared 40 m half-extent, well outside the 20 m basin.
        Assert.Equal(0f, WorldWater.DepthAt(35f, 0f, Bowl(-6f)), 3);
    }

    [Fact]
    public void OutsideEveryBodyThereIsNoSurfaceAtAll()
    {
        WorldWater.Set(Pool);
        Assert.Null(WorldWater.SurfaceAt(60f, 0f));
        Assert.NotNull(WorldWater.SurfaceAt(10f, 10f));
    }

    [Fact]
    public void ClearingTheRegionClearsTheWater()
    {
        WorldWater.Set(Pool);
        WorldWater.Set(null);
        Assert.Empty(WorldWater.Bodies);
        Assert.Equal(0f, WorldWater.DepthAt(0f, 0f, Bowl(-6f)), 3);
    }

    [Fact]
    public void TheWadeAndDrownThresholdsAreOrderedAndBothSurvivable()
    {
        // ⚠️ If these ever cross, the recovery service fires on ground the player is walking.
        Assert.True(WorldWater.WadeDepth < WorldWater.DrownDepth);
        Assert.True(WorldWater.WadeDepth > 0.5f, "shallow margins have to stay walkable");
    }

    [Fact]
    public void WadingDepthIsMeasuredFromTheFeetNotTheBed()
    {
        WorldWater.Set(Pool);
        WorldHeightfield bowl = Bowl(-6f);
        // Standing on the bottom: the full depth.
        Assert.Equal(6f, WorldWater.WadingDepthAt(0f, -6f, 0f, bowl), 2);
        // Feet two metres under the surface on something built in the basin: two metres.
        Assert.Equal(2f, WorldWater.WadingDepthAt(0f, -2f, 0f, bowl), 2);
    }

    [Fact]
    public void ABridgeOverDeepWaterIsDry()
    {
        // ⚠️ Terrain depth says six metres here; a deck above the waterline must read as dry, or the
        // recovery pulls the player off the bridge they are crossing.
        WorldWater.Set(Pool);
        Assert.Equal(0f, WorldWater.WadingDepthAt(0f, 0.5f, 0f, Bowl(-6f)), 3);
        Assert.Equal(6f, WorldWater.DepthAt(0f, 0f, Bowl(-6f)), 2);
    }

    [Fact]
    public void FeetBelowTheTerrainReadTheTerrainDepth()
    {
        // A foot a few centimetres into the collision mesh must not read deeper than the water is.
        WorldWater.Set(Pool);
        Assert.Equal(6f, WorldWater.WadingDepthAt(0f, -6.3f, 0f, Bowl(-6f)), 2);
    }

    [Fact]
    public void WithNoFieldThereIsNoWadingDepth()
    {
        WorldWater.Set(Pool);
        Assert.Equal(0f, WorldWater.WadingDepthAt(0f, -3f, 0f, null), 3);
    }
}
