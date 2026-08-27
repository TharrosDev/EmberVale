using System;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldTerrainMathTests
{
    [Theory]
    [InlineData(-26f, 0f)]
    [InlineData(26f, 0f)]
    [InlineData(0f, -26f)]
    [InlineData(0f, 26f)]
    public void SharedCellBoundary_IsExactlyFlat(float localX, float localZ)
    {
        float height = Height(localX, localZ, 0);
        Assert.Equal(0f, height);
    }

    [Fact]
    public void SameInputs_AreDeterministic()
    {
        Assert.Equal(Height(8.2f, -3.7f, 0), Height(8.2f, -3.7f, 0));
    }

    [Fact]
    public void RoadMask_FlattensTheSameHeightfieldSample()
    {
        float natural = Height(0f, 7f, 0);
        float road = Height(0f, 7f, 1);
        Assert.True(MathF.Abs(road) <= MathF.Abs(natural) * 0.121f);
    }

    [Fact]
    public void ValueNoise_IsContinuousAcrossCellCoordinates()
    {
        float left = WorldTerrainMath.ValueNoise(99, 12.499f, -4.25f);
        float right = WorldTerrainMath.ValueNoise(99, 12.501f, -4.25f);
        Assert.InRange(MathF.Abs(left - right), 0f, 0.01f);
    }

    private static float Height(float localX, float localZ, int roadAxis) =>
        WorldTerrainMath.Height(99, 100f + localX, -20f + localZ, localX, localZ,
            52f, 52f, 0.25f, 2.5f, roadAxis, 6f, 0f);
}
