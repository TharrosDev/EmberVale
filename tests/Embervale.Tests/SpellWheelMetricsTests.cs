using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Magic;
using Embervale.Settings;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Where the spell wheel sits, how big it is and what a wedge says.</summary>
public class SpellWheelMetricsTests
{
    private static readonly Vector2 Handheld = new(853f, 533f);
    private static readonly Vector2 Hd = new(1280f, 720f);
    private static readonly Vector2 FullHd = new(1920f, 1080f);

    // The canvas the HUD lays out in on a 16:9 screen.
    private static readonly Vector2 Logical = new(1600f, 900f);

    [Theory]
    [InlineData(SettingsMath.HudScaleMin)]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(SettingsMath.HudScaleMax)]
    public void TheWholeBlock_FitsEveryView_AtEveryHudScale(float hudScale)
    {
        // With the widest safe zone as well: the view the wheel lays out in is what is left of it.
        foreach (Vector2 screen in new[] { Handheld, Hd, Logical, FullHd })
        {
            foreach (float safe in new[] { 0f, SettingsMath.HudSafeZoneMax })
            {
                Vector2 view = screen * (1f - (2f * safe));
                float radius = SpellWheelMetrics.Radius(view, hudScale);
                Vector2 centre = SpellWheelMetrics.Centre(view, radius);
                float reach = radius * SpellWheelRules.FanEdge;
                string where = $"at {screen}, scale {hudScale}, safe {safe}";

                Assert.True(radius >= SpellWheelMetrics.MinRadius);
                Assert.True(centre.X - reach >= SpellWheelMetrics.Margin - 0.01f);
                Assert.True(centre.X + reach <= view.X - SpellWheelMetrics.Margin + 0.01f);

                // The legend over the wheel, with the names' room between it and the fan.
                float legendTop = SpellWheelMetrics.LegendTop(centre, radius);
                Assert.True(legendTop >= SpellWheelMetrics.Margin - 0.01f, $"legend off the view {where}");
                Assert.True(
                    legendTop + SpellWheelMetrics.LegendHeight + SpellWheelMetrics.Gap + SpellWheelMetrics.LabelRoom
                    <= centre.Y - reach + 0.01f, $"legend on the fan's names {where}");

                // The readout under it, wherever the open fan has pushed it.
                for (int school = -1; school < SpellWheelRules.Schools.Count; school++)
                {
                    foreach (int count in new[] { 1, 3, 5, 8 })
                    {
                        float bottom = SpellWheelMetrics.ReadoutTop(centre, radius, school, count) + SpellWheelMetrics.ReadoutHeight;
                        Assert.True(bottom <= view.Y - SpellWheelMetrics.Margin + 0.01f, $"readout off the view {where}, fan {school}x{count}");
                    }
                }

                Assert.True(SpellWheelMetrics.ReadoutWidth(view, radius) <= view.X - (SpellWheelMetrics.Margin * 2f) + 0.01f);
            }
        }
    }

    [Fact]
    public void TheRim_SpansNearHalfTheScreen_AndFollowsTheHudScale_UntilTheViewRunsOut()
    {
        // The outer ring's diameter is 45 to 50 percent of the screen's height, whatever the screen.
        foreach (Vector2 screen in new[] { Handheld, Hd, Logical, FullHd })
        {
            float share = SpellWheelMetrics.Radius(screen, 1f) * 2f / screen.Y;
            Assert.InRange(share, 0.45f, 0.50f);
        }

        Assert.Equal(Hd.Y * SpellWheelMetrics.RimShare * 0.75f, SpellWheelMetrics.Radius(Hd, 0.75f), 3);
        Assert.True(SpellWheelMetrics.Radius(Hd, 1.25f) > SpellWheelMetrics.Radius(Hd, 1f));

        // A scale the view has no room for is capped, not clipped.
        Assert.True(SpellWheelMetrics.Radius(Handheld, 1.5f) < Handheld.Y * SpellWheelMetrics.RimShare * 1.5f);

        // Nonsense in, something drawable out.
        Assert.Equal(SpellWheelMetrics.Radius(Hd, 1f), SpellWheelMetrics.Radius(Hd, 0f), 3);
        Assert.Equal(SpellWheelMetrics.Radius(Hd, 1f), SpellWheelMetrics.Radius(Hd, float.NaN), 3);
        Assert.Equal(SpellWheelMetrics.MinRadius, SpellWheelMetrics.Radius(Vector2.Zero, 1f), 3);
        Assert.Equal(SpellWheelMetrics.MinRadius, SpellWheelMetrics.Radius(new Vector2(float.NaN, float.NaN), 1f), 3);
    }

    [Fact]
    public void TheWheel_SitsOnTheCentreOfTheScreen()
    {
        foreach (Vector2 screen in new[] { Hd, Logical, FullHd })
        {
            Vector2 centre = SpellWheelMetrics.Centre(screen, SpellWheelMetrics.Radius(screen, 1f));
            Assert.Equal(screen.X * 0.5f, centre.X, 3);
            Assert.Equal(screen.Y * 0.5f, centre.Y, 3);
        }

        // The handheld view is the tight one: it may lift, and by no more than a few lines.
        Vector2 handheld = SpellWheelMetrics.Centre(Handheld, SpellWheelMetrics.Radius(Handheld, 1f));
        Assert.Equal(426.5f, handheld.X, 3);
        Assert.InRange(handheld.Y, 266.5f - 24f, 266.5f);
    }

    [Fact]
    public void TheReadout_SitsUnderTheRim_AndDropsOnlyForAFanThatHangsLower()
    {
        float radius = SpellWheelMetrics.Radius(Hd, 1f);
        Vector2 centre = SpellWheelMetrics.Centre(Hd, radius);
        float close = centre.Y + radius + SpellWheelMetrics.Gap;

        Assert.Equal(close, SpellWheelMetrics.ReadoutTop(centre, radius), 3);

        // Fire is straight up and Frost and Necrotic are in the top half: the plate stays put.
        Assert.Equal(close, SpellWheelMetrics.ReadoutTop(centre, radius, 0, 4), 3);
        Assert.Equal(close, SpellWheelMetrics.ReadoutTop(centre, radius, 1, 4), 3);
        Assert.Equal(close, SpellWheelMetrics.ReadoutTop(centre, radius, 5, 4), 3);

        // Arcane is straight down: the plate clears the whole fan and its names.
        Assert.Equal(SpellWheelRules.FanEdge, SpellWheelMetrics.FanDrop(3, 4), 4);
        Assert.Equal(
            centre.Y + (radius * SpellWheelRules.FanEdge) + SpellWheelMetrics.LabelRoom + SpellWheelMetrics.Gap,
            SpellWheelMetrics.ReadoutTop(centre, radius, 3, 4), 3);

        // Lightning and Nature hang part of the way, and as far as each other.
        Assert.InRange(SpellWheelMetrics.FanDrop(2, 3), 1f, SpellWheelRules.FanEdge);
        Assert.Equal(SpellWheelMetrics.FanDrop(2, 3), SpellWheelMetrics.FanDrop(4, 3), 4);
        Assert.Equal(0f, SpellWheelMetrics.FanDrop(0, 3), 4);
        Assert.Equal(0f, SpellWheelMetrics.FanDrop(-1, 3), 4);
        Assert.Equal(0f, SpellWheelMetrics.FanDrop(3, 0), 4);
    }

    [Fact]
    public void TextSizes_GrowWithTheRim_BetweenTheirFloorsAndWhatThePlateHolds()
    {
        Assert.Equal(18, SpellWheelMetrics.NameSize(84f));
        Assert.Equal(12, SpellWheelMetrics.TextSize(84f));
        Assert.Equal(24, SpellWheelMetrics.NameSize(207f));
        Assert.Equal(16, SpellWheelMetrics.TextSize(207f));
        Assert.Equal(24, SpellWheelMetrics.NameSize(400f));
        Assert.Equal(16, SpellWheelMetrics.TextSize(400f));
    }

    [Theory]
    [InlineData("Emberlash", "Emberlash", "")]
    [InlineData("Flame Lance", "Flame", "Lance")]
    [InlineData("Ball of Lightning", "Ball of", "Lightning")]
    [InlineData("  Blink ", "Blink", "")]
    [InlineData("", "", "")]
    public void AFanName_BreaksAtTheSpaceNearestItsMiddle(string name, string first, string second)
    {
        Assert.Equal((first, second), SpellWheelMetrics.NameLines(name));
        Assert.Equal((string.Empty, string.Empty), SpellWheelMetrics.NameLines(null));
    }

    [Fact]
    public void Direction_IsTheInverseOfTheRulesAngle()
    {
        for (int degrees = 0; degrees < 360; degrees += 15)
        {
            Vector2 direction = SpellWheelMetrics.Direction(degrees);
            Assert.Equal(1f, direction.Length(), 4);
            float back = SpellWheelRules.AngleDegrees(direction);
            float off = MathF.Abs((((back - degrees) + 540f) % 360f) - 180f);
            Assert.True(off < 0.01f, $"{degrees} came back as {back}");
        }

        Assert.Equal(new Vector2(0f, -1f).X, SpellWheelMetrics.Direction(0f).X, 4);
        Assert.Equal(-1f, SpellWheelMetrics.Direction(0f).Y, 4);
        Assert.Equal(1f, SpellWheelMetrics.Direction(90f).X, 4);

        // The engine's arcs start at +X and run clockwise on a screen: straight up is a quarter turn back.
        Assert.Equal(-MathF.PI / 2f, SpellWheelMetrics.ArcAngle(0f), 4);
        Assert.Equal(0f, SpellWheelMetrics.ArcAngle(90f), 4);
    }

    [Fact]
    public void WhatIsDrawnInAWedge_IsWhatTheRulesPickThere()
    {
        // Favourites and schools: the drawn centre of wedge i picks wedge i.
        for (int i = 0; i < SpellWheelRules.FavouriteWedges; i++)
        {
            float angle = SpellWheelMetrics.WedgeCentre(i, SpellWheelRules.FavouriteWedges);
            Assert.Equal(i, SpellWheelRules.Wedge(angle, SpellWheelRules.FavouriteWedges));
        }

        for (int i = 0; i < SpellWheelRules.Schools.Count; i++)
        {
            Assert.Equal(i, SpellWheelRules.Wedge(SpellWheelMetrics.WedgeCentre(i, SpellWheelRules.Schools.Count), SpellWheelRules.Schools.Count));
        }

        // Fans: every spell of every school, for every fan size a school could reach.
        for (int school = 0; school < SpellWheelRules.Schools.Count; school++)
        {
            for (int count = 1; count <= 18; count++)
            {
                for (int index = 0; index < count; index++)
                {
                    float angle = ((SpellWheelMetrics.FanCentre(school, index, count) % 360f) + 360f) % 360f;
                    Assert.Equal(index, SpellWheelRules.FanIndex(angle, school, count));
                }
            }
        }
    }

    [Fact]
    public void TheContentRadii_SitInsideTheirRings()
    {
        Assert.InRange(SpellWheelMetrics.FavouriteRadius, SpellWheelRules.DeadZone, SpellWheelRules.InnerEdge);
        Assert.InRange(SpellWheelMetrics.SchoolRadius, SpellWheelRules.InnerEdge, SpellWheelRules.OuterEdge);
        Assert.InRange(SpellWheelMetrics.FanRadius, SpellWheelRules.OuterEdge + SpellWheelMetrics.FanGap, SpellWheelRules.FanEdge);
    }

    [Theory]
    [InlineData(84f)]
    [InlineData(150f)]
    [InlineData(225f)]
    public void AGlyph_FitsItsWedge_AndNeighboursDoNotTouch(float radius)
    {
        // Favourites: eight discs round the inner ring.
        float depth = radius * (SpellWheelRules.InnerEdge - SpellWheelRules.DeadZone);
        float at = radius * SpellWheelMetrics.FavouriteRadius;
        float side = SpellWheelMetrics.GlyphSide(at, 45f, depth);
        Assert.True(side <= depth);
        Assert.True(side >= 18f, $"a favourite glyph is {side:0.0} px at a rim of {radius}");
        float apart = (SpellWheelMetrics.Direction(0f) * at).DistanceTo(SpellWheelMetrics.Direction(45f) * at);
        Assert.True(apart > side + 4f);

        // The narrowest fan wedge: the minimum the rules give a spell.
        float fanDepth = radius * (SpellWheelRules.FanEdge - SpellWheelRules.OuterEdge - SpellWheelMetrics.FanGap);
        float fanAt = radius * SpellWheelMetrics.FanRadius;
        float fanSide = SpellWheelMetrics.GlyphSide(fanAt, SpellWheelRules.MinFanSpellDegrees, fanDepth);
        Assert.True(fanSide <= fanDepth);
        Assert.True(fanSide >= 18f, $"a fan glyph is {fanSide:0.0} px at a rim of {radius}");
        float fanApart = (SpellWheelMetrics.Direction(0f) * fanAt).DistanceTo(
            SpellWheelMetrics.Direction(SpellWheelRules.MinFanSpellDegrees) * fanAt);
        Assert.True(fanApart > fanSide + 4f);
    }

    [Fact]
    public void ASector_IsOneClosedOutline_OuterArcThenInnerArcBack()
    {
        var centre = new Vector2(100f, 100f);
        Vector2[] points = SpellWheelMetrics.Sector(centre, 20f, 50f, -30f, 30f, 5f);
        Assert.Equal(26, points.Length); // twelve chords: thirteen points a side

        int half = points.Length / 2;
        for (int i = 0; i < half; i++)
        {
            Assert.Equal(50f, points[i].DistanceTo(centre), 2);
            Assert.Equal(20f, points[half + i].DistanceTo(centre), 2);
        }

        // The outline closes on itself: the last inner point is at the first outer point's angle.
        Assert.Equal(330f, SpellWheelRules.AngleDegrees(points[0] - centre), 1);
        Assert.Equal(30f, SpellWheelRules.AngleDegrees(points[half - 1] - centre), 1);
        Assert.Equal(30f, SpellWheelRules.AngleDegrees(points[half] - centre), 1);
        Assert.Equal(330f, SpellWheelRules.AngleDegrees(points[^1] - centre), 1);

        // No point repeats, which is what the engine's triangulator refuses.
        Assert.Equal(points.Length, points.Distinct().Count());

        // A sliver still has an outline.
        Assert.True(SpellWheelMetrics.Sector(centre, 20f, 50f, 0f, 1f).Length >= 6);
    }

    [Fact]
    public void AWedgesState_NamesTheReasonThatLastsLongest()
    {
        Assert.Equal(SpellWheelCellState.Empty, SpellWheelMetrics.State(false, true, 3f, 0f, 10f));
        Assert.Equal(SpellWheelCellState.Locked, SpellWheelMetrics.State(true, true, 3f, 0f, 10f));
        Assert.Equal(SpellWheelCellState.Cooling, SpellWheelMetrics.State(true, false, 3f, 0f, 10f));
        Assert.Equal(SpellWheelCellState.Unaffordable, SpellWheelMetrics.State(true, false, 0f, 9.9f, 10f));
        Assert.Equal(SpellWheelCellState.Ready, SpellWheelMetrics.State(true, false, 0f, 10f, 10f));
        Assert.Equal(SpellWheelCellState.Ready, SpellWheelMetrics.State(true, false, 0f, 0f, 0f));
    }

    [Fact]
    public void TheCooldownWipe_IsTheShareStillToRun()
    {
        Assert.Equal(0f, SpellWheelMetrics.CooldownFraction(0f, 8f));
        Assert.Equal(0f, SpellWheelMetrics.CooldownFraction(-1f, 8f));
        Assert.Equal(0f, SpellWheelMetrics.CooldownFraction(float.NaN, 8f));
        Assert.Equal(0.5f, SpellWheelMetrics.CooldownFraction(4f, 8f), 4);
        Assert.Equal(1f, SpellWheelMetrics.CooldownFraction(12f, 8f), 4);
        Assert.Equal(1f, SpellWheelMetrics.CooldownFraction(2f, 0f), 4);

        Assert.Equal(0, SpellWheelMetrics.Numeral(0f));
        Assert.Equal(1, SpellWheelMetrics.Numeral(0.1f));
        Assert.Equal(9, SpellWheelMetrics.Numeral(9f));
        Assert.Equal(10, SpellWheelMetrics.Numeral(9.1f));
        Assert.Equal(99, SpellWheelMetrics.Numeral(600f));
    }

    [Fact]
    public void TheBloom_EndsAtFullSize()
    {
        Assert.Equal(SpellWheelMetrics.BloomFrom, SpellWheelMetrics.Bloom(0f), 4);
        Assert.Equal(1f, SpellWheelMetrics.Bloom(1f), 4);
        Assert.Equal(1f, SpellWheelMetrics.Bloom(5f), 4);
        Assert.True(SpellWheelMetrics.Bloom(0.5f) > SpellWheelMetrics.Bloom(0.25f));

        // Reduced motion gives a zero duration, which is complete at once.
        Assert.Equal(1f, SpellWheelMetrics.Bloom(UiMotion.Progress(0f, 0f)), 4);
    }

    [Fact]
    public void EveryStringTheWheelShows_IsInTheCatalogue()
    {
        // Two of the wheel's keys are picked by a condition inside the call, which the catalogue
        // test's pattern (a literal straight after the bracket) cannot see.
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Embervale.sln")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Could not find Embervale.sln");
        }

        var catalogue = File.ReadLines(Path.Combine(root, "data", "locale", "strings.csv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split(',')[0])
            .ToHashSet();

        string source = File.ReadAllText(Path.Combine(root, "src", "UI", "SpellWheel.cs"));
        var keys = Regex.Matches(source, @"""((?:wheel|school|spellbook|hud|magic)\.[a-z0-9_.]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.True(keys.Count >= 20, $"only {keys.Count} keys found; has the wheel stopped using Loc?");
        Assert.All(keys, key => Assert.Contains(key, catalogue));
    }
}
