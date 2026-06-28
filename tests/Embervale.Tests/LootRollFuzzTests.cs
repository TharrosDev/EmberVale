using Embervale.Items;
using Embervale.Loot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Fuzz/property harness over the loot roll kernels. <see cref="LootRarity.Roll"/> and
/// <see cref="AffixDefinition.Roll"/> are driven by Godot's native <c>RandomNumberGenerator</c>
/// (exercised in-engine), but the pure cores — <see cref="LootRarity.Select"/> and
/// <see cref="AffixDefinition.BlendValue"/> — take the uniform sample directly, so the whole
/// <c>[0,1)</c> sample space is swept here to prove rolls stay in-bounds and behave monotonically.
/// </summary>
public class LootRollFuzzTests
{
    private const int Samples = 10_000;

    // --- LootRarity.Select --------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(5f)]
    [InlineData(-3f)] // negative quality is clamped to 0, must not throw or escape the enum
    public void Select_AlwaysReturnsAValidRarity(float quality)
    {
        for (int i = 0; i <= Samples; i++)
        {
            float roll01 = i / (float)Samples;
            ItemRarity rarity = LootRarity.Select(quality, roll01);
            Assert.InRange((int)rarity, (int)ItemRarity.Common, (int)ItemRarity.Legendary);
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(5f)]
    public void Select_IsMonotonicInRoll(float quality)
    {
        int previousTier = -1;
        for (int i = 0; i <= Samples; i++)
        {
            float roll01 = i / (float)Samples;
            int tier = (int)LootRarity.Select(quality, roll01);
            Assert.True(tier >= previousTier,
                $"tier must not decrease as the sample rises (quality {quality}, roll {roll01})");
            previousTier = tier;
        }
    }

    [Fact]
    public void Select_HigherQualityBiasesTowardHigherTiers()
    {
        float lowLegendary = Fraction(0f, ItemRarity.Legendary);
        float highLegendary = Fraction(5f, ItemRarity.Legendary);
        float lowCommon = Fraction(0f, ItemRarity.Common);
        float highCommon = Fraction(5f, ItemRarity.Common);

        Assert.True(highLegendary > lowLegendary,
            $"quality should raise the legendary rate ({highLegendary} !> {lowLegendary})");
        Assert.True(highCommon < lowCommon,
            $"quality should lower the common rate ({highCommon} !< {lowCommon})");
    }

    private static float Fraction(float quality, ItemRarity target)
    {
        int hits = 0;
        for (int i = 0; i < Samples; i++)
        {
            float roll01 = i / (float)Samples;
            if (LootRarity.Select(quality, roll01) == target)
            {
                hits++;
            }
        }

        return hits / (float)Samples;
    }

    // --- AffixDefinition.BlendValue -----------------------------------------

    [Theory]
    [InlineData(1f, 5f)]
    [InlineData(0f, 0f)]      // degenerate equal bounds
    [InlineData(-10f, 10f)]   // spans zero
    [InlineData(2f, 2.0001f)] // razor-thin range
    public void BlendValue_StaysWithinBounds(float min, float max)
    {
        for (float quality = 0f; quality <= 1f; quality += 0.1f)
        {
            for (int i = 0; i <= Samples; i++)
            {
                float roll01 = i / (float)Samples;
                float value = AffixDefinition.BlendValue(min, max, quality, roll01);
                Assert.InRange(value, min, max);
            }
        }
    }

    [Fact]
    public void BlendValue_IsNonDecreasingInRollAndQuality()
    {
        float prevByRoll = float.NegativeInfinity;
        for (int i = 0; i <= Samples; i++)
        {
            float value = AffixDefinition.BlendValue(1f, 5f, 0.5f, i / (float)Samples);
            Assert.True(value >= prevByRoll, "value must not fall as the sample rises");
            prevByRoll = value;
        }

        float prevByQuality = float.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            float value = AffixDefinition.BlendValue(1f, 5f, i / 100f, 0.5f);
            Assert.True(value >= prevByQuality, "value must not fall as quality rises");
            prevByQuality = value;
        }
    }

    [Fact]
    public void BlendValue_ReachesNearMaxAtTopQualityAndRoll()
    {
        float top = AffixDefinition.BlendValue(1f, 5f, 1f, 1f);
        Assert.Equal(5f, top, 0.0001f);

        float bottom = AffixDefinition.BlendValue(1f, 5f, 0f, 0f);
        Assert.Equal(1f, bottom, 0.0001f);
    }

    [Fact]
    public void BlendValue_DegenerateInvertedBounds_ClampToMin()
    {
        // min > max should never produce something outside [max, min]; we pin it to min.
        Assert.Equal(9f, AffixDefinition.BlendValue(9f, 2f, 1f, 1f), 0.0001f);
    }
}
