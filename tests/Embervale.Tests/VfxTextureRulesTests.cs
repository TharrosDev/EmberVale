using System;
using System.Collections.Generic;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The effect textures are painted from these functions, and the shaders lean on three of their
/// properties: nothing leaves 0..1 (a texture byte), the noise tiles (it is scrolled forever), and a
/// mask is empty at its border (a sprite that is not is a visible square in the air).
/// </summary>
public class VfxTextureRulesTests
{
    public static readonly TheoryData<string> Masks = new()
    {
        "Dot", "Streak", "Shard", "Leaf", "Puff", "Rune", "Scorch", "ScorchHeat", "Frost", "Roots",
        "Crystal", "Glint", "Rays",
    };

    private static Func<float, float, float> Painter(string name) => name switch
    {
        "Dot" => VfxTextureRules.Dot,
        "Streak" => VfxTextureRules.Streak,
        "Shard" => VfxTextureRules.Shard,
        "Leaf" => VfxTextureRules.Leaf,
        "Puff" => VfxTextureRules.Puff,
        "Rune" => VfxTextureRules.Rune,
        "Scorch" => VfxTextureRules.Scorch,
        "ScorchHeat" => VfxTextureRules.ScorchHeat,
        "Frost" => VfxTextureRules.Frost,
        "Roots" => VfxTextureRules.Roots,
        "Crystal" => VfxTextureRules.Crystal,
        "Glint" => VfxTextureRules.Glint,
        "Rays" => VfxTextureRules.Rays,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Masks))]
    public void EveryPixelIsAValidByte(string name)
    {
        Func<float, float, float> paint = Painter(name);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                float value = paint((x + 0.5f) / 48f, (y + 0.5f) / 48f);
                Assert.False(float.IsNaN(value), $"{name} is NaN at {x},{y}");
                Assert.InRange(value, 0f, 1f);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Masks))]
    public void EveryMaskIsEmptyAtItsBorder(string name)
    {
        Func<float, float, float> paint = Painter(name);
        const int Size = 64;
        for (int i = 0; i < Size; i++)
        {
            float along = (i + 0.5f) / Size;
            float edge = 0.5f / Size;
            Assert.True(paint(along, edge) < 0.02f, $"{name} is not empty along its top edge");
            Assert.True(paint(along, 1f - edge) < 0.02f, $"{name} is not empty along its bottom edge");
            Assert.True(paint(edge, along) < 0.02f, $"{name} is not empty along its left edge");
            Assert.True(paint(1f - edge, along) < 0.02f, $"{name} is not empty along its right edge");
        }
    }

    [Theory]
    [MemberData(nameof(Masks))]
    public void EveryMaskDrawsSomething(string name)
    {
        Func<float, float, float> paint = Painter(name);
        float most = 0f;
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                most = MathF.Max(most, paint((x + 0.5f) / 64f, (y + 0.5f) / 64f));
            }
        }

        Assert.True(most > 0.6f, $"{name} never gets brighter than {most}");
    }

    [Fact]
    public void TheDotIsBrightestAtItsCentre()
    {
        Assert.Equal(1f, VfxTextureRules.Dot(0.5f, 0.5f), 3);
        Assert.True(VfxTextureRules.Dot(0.5f, 0.5f) > VfxTextureRules.Dot(0.7f, 0.5f));
        Assert.True(VfxTextureRules.Dot(0.7f, 0.5f) > VfxTextureRules.Dot(0.9f, 0.5f));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(4, 4)]
    [InlineData(7, 3)]
    public void TheNoiseTiles(int period, int octaves)
    {
        // The value at one edge is the value at the other, in both directions, at every octave.
        for (int i = 0; i < 40; i++)
        {
            float t = i / 40f;
            Assert.Equal(VfxTextureRules.Fbm(0f, t, period, octaves, 1), VfxTextureRules.Fbm(1f, t, period, octaves, 1), 4);
            Assert.Equal(VfxTextureRules.Fbm(t, 0f, period, octaves, 1), VfxTextureRules.Fbm(t, 1f, period, octaves, 1), 4);
        }
    }

    [Fact]
    public void TheNoiseIsNotFlat()
    {
        var seen = new HashSet<int>();
        float least = 1f;
        float most = 0f;
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float value = VfxTextureRules.Fbm(x / 32f, y / 32f, 4, 4, 1);
                Assert.InRange(value, 0f, 1f);
                least = MathF.Min(least, value);
                most = MathF.Max(most, value);
                seen.Add((int)(value * 255f));
            }
        }

        Assert.True(most - least > 0.35f, "the noise has too little contrast to erode a flame with");
        Assert.True(seen.Count > 40);
    }

    [Fact]
    public void TheNoiseIsRepeatableAndSeeded()
    {
        Assert.Equal(VfxTextureRules.Fbm(0.3f, 0.7f, 4, 4, 1), VfxTextureRules.Fbm(0.3f, 0.7f, 4, 4, 1));
        Assert.NotEqual(VfxTextureRules.Fbm(0.3f, 0.7f, 4, 4, 1), VfxTextureRules.Fbm(0.3f, 0.7f, 4, 4, 2));
        Assert.InRange(VfxTextureRules.Hash(-3, 9, 4), 0f, 1f);
    }

    [Fact]
    public void TheRuneCircleHasItsRings()
    {
        // On the outer ring, straight out along +X from the centre (radius 0.93 of the half size).
        Assert.True(VfxTextureRules.Rune(0.5f + (0.93f * 0.5f), 0.5f) > 0.8f);

        // In the open band between the inner ring and the hexagram's points, nothing.
        Assert.True(VfxTextureRules.Rune(0.5f, 0.5f) < 0.05f);
    }
}
