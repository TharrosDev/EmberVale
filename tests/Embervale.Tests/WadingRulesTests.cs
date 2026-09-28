using System;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Depth-graded wading. What is worth pinning: the bands are ordered and match the water contract's
/// two thresholds, the speed curve is continuous and never speeds anyone up, and the push-back beats
/// a sprint past the wade limit — the property that makes the "too deep" warning true.
/// </summary>
public sealed class WadingRulesTests
{
    private static readonly WadingTuning T = WadingTuning.Default;

    [Theory]
    [InlineData(0f, WadingBand.Dry)]
    [InlineData(0.05f, WadingBand.Dry)]
    [InlineData(0.2f, WadingBand.Ankle)]
    [InlineData(0.5f, WadingBand.Knee)]
    [InlineData(0.9f, WadingBand.Waist)]
    [InlineData(1.1f, WadingBand.Waist)]
    [InlineData(1.3f, WadingBand.TooDeep)]
    [InlineData(1.9f, WadingBand.Drowning)]
    [InlineData(4.5f, WadingBand.Drowning)]
    public void BandsFollowDepth(float depth, WadingBand expected) =>
        Assert.Equal(expected, WadingRules.BandFor(depth));

    [Fact]
    public void ANaNDepthIsDryAndFullSpeed()
    {
        Assert.Equal(WadingBand.Dry, WadingRules.BandFor(float.NaN));
        Assert.Equal(1f, WadingRules.SpeedScale(float.NaN));
        Assert.Equal(0f, WadingRules.PushBack(float.NaN));
    }

    [Fact]
    public void TheDefaultBandsSitInsideTheWaterContract()
    {
        Assert.True(T.AnkleDepth < T.KneeDepth);
        Assert.True(T.KneeDepth < T.WaistDepth);
        Assert.True(T.WaistDepth < T.WarnDepth);
        Assert.True(T.WarnDepth < WorldWater.WadeDepth, "the warning must come while there is ground to turn back on");
    }

    [Fact]
    public void SpeedFallsMonotonicallyAndContinuously()
    {
        float previous = WadingRules.SpeedScale(0f);
        Assert.Equal(1f, previous);
        for (float depth = 0.005f; depth <= 2.5f; depth += 0.005f)
        {
            float scale = WadingRules.SpeedScale(depth);
            Assert.True(scale <= previous + 1e-5f, $"speed rose at {depth:0.000} m");
            Assert.True(previous - scale < 0.02f, $"speed stepped at {depth:0.000} m ({previous} -> {scale})");
            Assert.InRange(scale, T.DeepSpeed - 1e-5f, 1f);
            previous = scale;
        }
    }

    [Fact]
    public void SpeedHitsItsAnchorsAtTheBandEdges()
    {
        Assert.Equal(1f, WadingRules.SpeedScale(T.AnkleDepth), 4);
        Assert.Equal(T.KneeSpeed, WadingRules.SpeedScale(T.KneeDepth), 4);
        Assert.Equal(T.WaistSpeed, WadingRules.SpeedScale(T.WaistDepth), 4);
        Assert.Equal(T.LimitSpeed, WadingRules.SpeedScale(WorldWater.WadeDepth), 4);
        Assert.Equal(T.DeepSpeed, WadingRules.SpeedScale(WorldWater.WadeDepth + T.DeepRamp), 4);
        Assert.Equal(T.DeepSpeed, WadingRules.SpeedScale(3f), 4);
    }

    [Fact]
    public void QuantizedSpeedIsOnTheStepGridAndNearTheCurve()
    {
        for (float depth = 0f; depth <= 2f; depth += 0.013f)
        {
            float q = WadingRules.QuantizedSpeedScale(depth);
            Assert.True(MathF.Abs(q - WadingRules.SpeedScale(depth)) <= (T.SpeedStep * 0.5f) + 1e-5f);
            if (q < 1f)
            {
                float steps = q / T.SpeedStep;
                Assert.True(MathF.Abs(steps - MathF.Round(steps)) < 1e-3f, $"{q} is off the grid");
            }
        }
    }

    [Fact]
    public void PushBackIsZeroUntilTheWarningAndFullAtTheLimit()
    {
        Assert.Equal(0f, WadingRules.PushBack(0.5f));
        Assert.Equal(0f, WadingRules.PushBack(T.WarnDepth));
        float mid = WadingRules.PushBack((T.WarnDepth + WorldWater.WadeDepth) * 0.5f);
        Assert.InRange(mid, 0.01f, T.PushBackSpeed - 0.01f);
        Assert.Equal(T.PushBackSpeed, WadingRules.PushBack(WorldWater.WadeDepth), 4);
        Assert.Equal(T.PushBackSpeed, WadingRules.PushBack(1.6f), 4);
    }

    [Fact]
    public void PastTheLimitThePushOutPullsASprint()
    {
        // ⚠️ The shipped motor: 5 m/s base, 1.6 sprint multiplier (LocomotionComponent defaults).
        const float Base = 5f;
        const float Sprint = 1.6f;
        float deep = WorldWater.WadeDepth + T.DeepRamp;
        Assert.True(WadingRules.PushBack(deep) > Base * Sprint * WadingRules.SpeedScale(deep),
            "a sprint must not carry the player through the push-back into the drown band");
    }

    [Fact]
    public void TheWarningFiresOnceOnTheWayIn()
    {
        Assert.True(WadingRules.CrossedWarning(0.8f, 0.95f));
        Assert.False(WadingRules.CrossedWarning(0.95f, 1.0f));
        Assert.False(WadingRules.CrossedWarning(1.0f, 0.8f));
    }

    [Fact]
    public void ShallowerPointsDownTheDepthGradient()
    {
        // Depth rises with +X: shallower is -X.
        (float x, float z) = WadingRules.ShallowerDirection((px, _) => 1f + (0.1f * px), 0f, 0f);
        Assert.Equal(-1f, x, 4);
        Assert.Equal(0f, z, 4);

        // Depth rises towards the centre of a bowl: from (5, 5), shallower is outward along +X+Z.
        (x, z) = WadingRules.ShallowerDirection((px, pz) => 3f - (0.05f * ((px * px) + (pz * pz))), 5f, 5f);
        Assert.Equal(MathF.Sqrt(0.5f), x, 3);
        Assert.Equal(MathF.Sqrt(0.5f), z, 3);
    }

    [Fact]
    public void AFlatBottomHasNoShallowerDirection()
    {
        Assert.Equal((0f, 0f), WadingRules.ShallowerDirection((_, _) => 1.3f, 2f, 7f));
    }

    [Fact]
    public void CustomTuningIsHonoured()
    {
        var slow = new WadingTuning { KneeSpeed = 0.5f };
        Assert.Equal(0.5f, WadingRules.SpeedScale(slow.KneeDepth, slow), 4);
        Assert.Equal(WadingBand.Knee, WadingRules.BandFor(0.2f, new WadingTuning { KneeDepth = 0.15f }));
    }
}
