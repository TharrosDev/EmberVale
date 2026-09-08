using System;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class EnvironmentMathTests
{
    [Theory]
    [InlineData(-1, 23)]
    [InlineData(24, 0)]
    [InlineData(49, 1)]
    public void ClockWraps(float hour, float expected) => Assert.Equal(expected, EnvironmentMath.Hour(hour));

    [Fact]
    public void NightKeyInterpolatesAcrossMidnight()
    {
        Assert.Equal(.5f, EnvironmentMath.KeyWeight(0, 20, 4), 5);
        Assert.Equal(0, EnvironmentMath.KeyWeight(20, 20, 4));
        Assert.True(EnvironmentMath.KeyWeight(3.99f, 20, 4) > .999f);
    }

    [Fact]
    public void BlendIsFrameRateIndependent()
    {
        float one = EnvironmentMath.BlendWeight(1, 4);
        float small = EnvironmentMath.BlendWeight(1f / 60f, 4);
        float accumulated = 1f - MathF.Pow(1 - small, 60);
        Assert.Equal(one, accumulated, 5);
        Assert.Equal(0, EnvironmentMath.BlendWeight(-1, 4));
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(.16f, 1)]
    [InlineData(.24f, .5f)]
    [InlineData(.32f, 0)]
    [InlineData(10, 0)]
    public void SnowIsBoundedAndTemperatureDriven(float temperature, float expected) =>
        Assert.Equal(expected, EnvironmentMath.SnowFraction(temperature), 5);
}
