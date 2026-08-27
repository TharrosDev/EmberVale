using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldSceneryMathTests
{
    [Fact]
    public void StableSeed_ReproducesTheSameRidge()
    {
        Assert.Equal(WorldSceneryMath.RidgeHeight(204, 7, 14f),
            WorldSceneryMath.RidgeHeight(204, 7, 14f));
    }

    [Fact]
    public void DifferentCells_DoNotRepeatTheSameSilhouette()
    {
        Assert.NotEqual(WorldSceneryMath.Hash(201, 4), WorldSceneryMath.Hash(202, 4));
    }

    [Theory]
    [InlineData(38, 0)]
    [InlineData(205, 11)]
    [InlineData(-1, 99)]
    public void Unit_IsAlwaysNormalised(int seed, int index)
    {
        float value = WorldSceneryMath.Unit(seed, index);
        Assert.InRange(value, 0f, 1f);
    }

    [Fact]
    public void RidgeVariation_StaysInsideAuthoredBudget()
    {
        const float authored = 14f;
        for (int i = 0; i < 128; i++)
        {
            Assert.InRange(WorldSceneryMath.RidgeHeight(204, i, authored),
                authored * 0.55f, authored * 1.30f);
        }
    }
}
