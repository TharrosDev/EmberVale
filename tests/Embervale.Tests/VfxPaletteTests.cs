using System;
using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The effect palette is derived from <see cref="SpellSchools.Color"/>, never authored beside it: a
/// fireball that is one orange in the spellbook and another in the air is two schools. Every derived
/// colour is pinned to its school's hue here.
/// </summary>
public class VfxPaletteTests
{
    public static readonly TheoryData<DamageType> Schools = new()
    {
        DamageType.Physical, DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic, DamageType.True,
    };

    [Theory]
    [MemberData(nameof(Schools))]
    public void EveryColourKeepsItsSchoolsHue(DamageType school)
    {
        Color tint = SpellSchools.Color(school);
        VfxSchoolColors colours = VfxPalette.For(school);

        // A grey has no hue to keep (the fallback school colour is one); everything else is pinned.
        if (tint.S < 0.01f)
        {
            return;
        }

        Assert.InRange(VfxPalette.HueDistanceDegrees(tint, colours.Core), 0f, VfxPalette.HueToleranceDegrees);
        Assert.InRange(VfxPalette.HueDistanceDegrees(tint, colours.Mid), 0f, VfxPalette.HueToleranceDegrees);
        Assert.InRange(VfxPalette.HueDistanceDegrees(tint, colours.Edge), 0f, VfxPalette.HueToleranceDegrees);
    }

    [Fact]
    public void TheToleranceIsTwelveDegrees()
    {
        Assert.Equal(12f, VfxPalette.HueToleranceDegrees);
    }

    [Theory]
    [MemberData(nameof(Schools))]
    public void TheCoreIsTheHottestAndPalest(DamageType school)
    {
        VfxSchoolColors colours = VfxPalette.For(school);

        Assert.True(colours.CoreEnergy > colours.MidEnergy);
        Assert.True(colours.MidEnergy > colours.EdgeEnergy);
        Assert.InRange(colours.CoreEnergy, 4f, 8f); // the design's HDR band for a core
        Assert.True(colours.EdgeEnergy > 1.2f);     // still past the glow threshold

        Assert.True(colours.Core.S <= colours.Mid.S);
        Assert.True(colours.Mid.S <= colours.Edge.S + 0.0001f);
        Assert.True(colours.Core.S > 0.1f);         // never white: a white core has no school
        Assert.InRange(colours.Halo, 0.3f, 0.4f);
    }

    [Theory]
    [MemberData(nameof(Schools))]
    public void AnEnemysCastIsDimmerInTheSameColours(DamageType school)
    {
        VfxSchoolColors mine = VfxPalette.For(school, byPlayer: true);
        VfxSchoolColors theirs = VfxPalette.For(school, byPlayer: false);

        Assert.Equal(VfxPalette.For(school), mine);
        Assert.Equal(mine.Core, theirs.Core);
        Assert.Equal(mine.Mid, theirs.Mid);
        Assert.Equal(mine.Edge, theirs.Edge);
        Assert.Equal(mine.CoreEnergy * 0.7f, theirs.CoreEnergy, 4);
        Assert.Equal(mine.MidEnergy * 0.7f, theirs.MidEnergy, 4);
        Assert.Equal(mine.EdgeEnergy * 0.7f, theirs.EdgeEnergy, 4);
        Assert.Equal(mine.Halo * 0.8f, theirs.Halo, 4);
    }

    [Fact]
    public void HueDistance_WrapsAroundTheWheel()
    {
        Color nearZero = Color.FromHsv(0.01f, 1f, 1f);
        Color nearOne = Color.FromHsv(0.99f, 1f, 1f);

        Assert.Equal(7.2f, VfxPalette.HueDistanceDegrees(nearZero, nearOne), 1);
        Assert.Equal(0f, VfxPalette.HueDistanceDegrees(nearZero, nearZero), 3);
        Assert.Equal(180f, VfxPalette.HueDistanceDegrees(Color.FromHsv(0f, 1f, 1f), Color.FromHsv(0.5f, 1f, 1f)), 1);
    }

    [Fact]
    public void TheSchoolsStayTellableApart()
    {
        // Deriving must not collapse two schools onto one colour: each pair of magic schools keeps
        // a visible gap in the effect's body colour.
        DamageType[] magic =
        {
            DamageType.Fire, DamageType.Frost, DamageType.Lightning,
            DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
        };

        for (int i = 0; i < magic.Length; i++)
        {
            for (int j = i + 1; j < magic.Length; j++)
            {
                Color a = VfxPalette.For(magic[i]).Mid;
                Color b = VfxPalette.For(magic[j]).Mid;
                float gap = MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B);
                Assert.True(gap > 0.15f, $"{magic[i]} and {magic[j]} draw as nearly the same colour ({gap:0.00}).");
            }
        }
    }
}
