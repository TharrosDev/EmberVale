using System;
using System.Collections.Generic;
using Embervale.UI;
using Embervale.World;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure logic behind the 39.5B HUD work: the tracked-objective readout (compass point + distance),
/// the minimap's clutter rule, and the HUD's mode-to-visibility table.
///
/// All three are things that look obviously right in a diff and are wrong at a bearing nobody tried —
/// which is the class of defect this repo cannot see, because <c>--play</c> cannot press a key and the
/// Godot MCP drives the editor rather than the running game (NOW.md invariant 8).
/// </summary>
public class HudReadoutTests
{
    // ── Compass points ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CardinalKey_DueNorthIsNorth_NotNortheast()
    {
        // The bucket for North straddles zero, so this is the case a truncating implementation
        // gets wrong — it lands every heading from 0° to 45° in NE and never returns N at all.
        Assert.Equal("hud.compass.n", CompassMath.CardinalKey(0f));
    }

    [Theory]
    [InlineData(0, "hud.compass.n")]
    [InlineData(45, "hud.compass.ne")]
    [InlineData(90, "hud.compass.e")]
    [InlineData(135, "hud.compass.se")]
    [InlineData(180, "hud.compass.s")]
    [InlineData(225, "hud.compass.sw")]
    [InlineData(270, "hud.compass.w")]
    [InlineData(315, "hud.compass.nw")]
    public void CardinalKey_MapsEachEighthToItsPoint(int degrees, string expected) =>
        Assert.Equal(expected, CompassMath.CardinalKey(Radians(degrees)));

    [Theory]
    [InlineData(22)]   // just inside the North bucket's upper edge
    [InlineData(-22)]  // just inside its lower edge, from the negative side
    [InlineData(338)]  // the same edge expressed as a positive angle (the bucket opens at 337.5°)
    public void CardinalKey_HoldsTheBucketToEitherSideOfNorth(int degrees) =>
        Assert.Equal("hud.compass.n", CompassMath.CardinalKey(Radians(degrees)));

    [Theory]
    [InlineData(23, "hud.compass.ne")]
    [InlineData(337, "hud.compass.nw")]
    public void CardinalKey_HandsOffAtTheBucketEdge(int degrees, string expected) =>
        Assert.Equal(expected, CompassMath.CardinalKey(Radians(degrees)));

    [Fact]
    public void CardinalKey_SurvivesUnwrappedAngles()
    {
        // BearingTo returns (-π, π], but nothing stops a caller handing over an accumulated angle.
        // Every return must still be a real key, or the tracker prints a raw one at the player.
        foreach (int degrees in new[] { -720, -359, 359, 721, 5000 })
        {
            Assert.Contains(CompassMath.CardinalKey(Radians(degrees)), CompassMath.CardinalKeys);
        }
    }

    [Fact]
    public void CardinalKeys_AreTheEightDistinctPoints()
    {
        // The validator arm enumerates this array, so its completeness IS the locale guarantee.
        Assert.Equal(8, CompassMath.CardinalKeys.Length);
        Assert.Equal(8, new HashSet<string>(CompassMath.CardinalKeys).Count);
    }

    // ── Distance readout ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(3.4f, "3")]
    [InlineData(319.6f, "320")]
    [InlineData(999.4f, "999")]
    public void Distance_UnderAKilometreIsWholeMetres(float metres, string expected)
    {
        (string value, string unit) = CompassMath.Distance(metres);
        Assert.Equal(expected, value);
        Assert.Equal("hud.unit.metres", unit);
    }

    [Theory]
    [InlineData(1000f, "1.0")]
    [InlineData(1450f, "1.5")]
    [InlineData(12_000f, "12.0")]
    public void Distance_AtAKilometreSwitchesUnit(float metres, string expected)
    {
        (string value, string unit) = CompassMath.Distance(metres);
        Assert.Equal(expected, value);
        Assert.Equal("hud.unit.kilometres", unit);
    }

    [Fact]
    public void Distance_ClampsNegativesRatherThanPrintingThem()
    {
        // A negative distance is not a thing the player can be shown. Guarding here rather than at
        // the call site means every future caller inherits it.
        (string value, string unit) = CompassMath.Distance(-12f);
        Assert.Equal("0", value);
        Assert.Equal("hud.unit.metres", unit);
    }

    [Fact]
    public void Distance_UsesInvariantDecimalSeparator()
    {
        // The value is composed into a locale string by Loc.TF, so a culture that formats 1,5 would
        // put a comma in the middle of the number rather than translating the unit.
        (string value, _) = CompassMath.Distance(1450f);
        Assert.Contains(".", value);
        Assert.DoesNotContain(",", value);
    }

    // ── Minimap clutter rule ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_DropsAnythingBeyondTheRadius()
    {
        var all = new List<MapPin>
        {
            Pin("near", 10f, MapTier.Detail),
            Pin("far", 200f, MapTier.Primary), // important, but not near: distance is the first filter
        };

        var into = new List<MapPin>();
        MinimapFilter.Select(all, Vector2.Zero, 48f, 10, into);

        Assert.Single(into);
        Assert.Equal("near", into[0].Id);
    }

    [Fact]
    public void Select_CapKeepsTheImportantOnes_NotTheFirstAdded()
    {
        // The failure this pins: truncating the unsorted list. The town is added last and is the one
        // thing on screen the player actually navigates by; a naive cap drops exactly that.
        var all = new List<MapPin>
        {
            Pin("stall_a", 5f, MapTier.Detail),
            Pin("stall_b", 6f, MapTier.Detail),
            Pin("stall_c", 7f, MapTier.Detail),
            Pin("town", 20f, MapTier.Primary),
        };

        var into = new List<MapPin>();
        MinimapFilter.Select(all, Vector2.Zero, 48f, 2, into);

        Assert.Equal(2, into.Count);
        Assert.Equal("town", into[0].Id);
    }

    [Fact]
    public void Select_BreaksTiesWithinATierByDistance()
    {
        var all = new List<MapPin>
        {
            Pin("further", 30f, MapTier.Secondary),
            Pin("closer", 4f, MapTier.Secondary),
        };

        var into = new List<MapPin>();
        MinimapFilter.Select(all, Vector2.Zero, 48f, 10, into);

        Assert.Equal("closer", into[0].Id);
        Assert.Equal("further", into[1].Id);
    }

    [Fact]
    public void Select_ClearsTheTargetList()
    {
        // The minimap reuses one list every refresh, so a Select that appended would grow without
        // bound and redraw stale pins forever.
        var into = new List<MapPin> { Pin("stale", 1f, MapTier.Primary) };
        MinimapFilter.Select(new List<MapPin>(), Vector2.Zero, 48f, 10, into);
        Assert.Empty(into);
    }

    [Fact]
    public void Select_WithNoRoomDrawsNothing()
    {
        var all = new List<MapPin> { Pin("town", 1f, MapTier.Primary) };
        var into = new List<MapPin>();
        MinimapFilter.Select(all, Vector2.Zero, 48f, 0, into);
        Assert.Empty(into);
    }

    [Fact]
    public void Rank_OrdersPrimaryAheadOfSecondaryAheadOfDetail() =>
        Assert.True(
            MinimapFilter.Rank(MapTier.Primary) < MinimapFilter.Rank(MapTier.Secondary) &&
            MinimapFilter.Rank(MapTier.Secondary) < MinimapFilter.Rank(MapTier.Detail));

    // ── HUD mode table ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ModeFor_NotPlayingIsInactiveWhateverElseIsTrue() =>
        Assert.Equal(HudMode.Inactive, HudVisibility.ModeFor(playing: false, menuOpen: true));

    [Fact]
    public void ModeFor_MenuBeatsExploration() =>
        Assert.Equal(HudMode.Menu, HudVisibility.ModeFor(playing: true, menuOpen: true));

    [Fact]
    public void ModeFor_PlainPlayIsExploration() =>
        Assert.Equal(HudMode.Exploration, HudVisibility.ModeFor(playing: true, menuOpen: false));

    [Fact]
    public void Inactive_ShowsNothing()
    {
        // The strongest single guarantee here: outside a session no group is on, so nothing from the
        // last world can survive into the main menu.
        Assert.False(HudVisibility.ShowsHud(HudMode.Inactive));
        Assert.False(HudVisibility.ShowsVitals(HudMode.Inactive));
        Assert.False(HudVisibility.ShowsNavigation(HudMode.Inactive));
        Assert.False(HudVisibility.ShowsPrompt(HudMode.Inactive));
        Assert.False(HudVisibility.ShowsCombat(HudMode.Inactive));
    }

    [Fact]
    public void Menu_KeepsVitalsAndDropsEverythingElse()
    {
        // The vitals exception is deliberate (a potion is drunk from the inventory). The prompt is
        // the one that MUST be off: a blocking menu pauses the tree, so the key it offers does
        // nothing (§31).
        Assert.True(HudVisibility.ShowsVitals(HudMode.Menu));
        Assert.False(HudVisibility.ShowsPrompt(HudMode.Menu));
        Assert.False(HudVisibility.ShowsNavigation(HudMode.Menu));
        Assert.False(HudVisibility.ShowsCombat(HudMode.Menu));
        Assert.False(HudVisibility.ShowsHud(HudMode.Menu));
    }

    [Fact]
    public void Exploration_ShowsEverything()
    {
        Assert.True(HudVisibility.ShowsHud(HudMode.Exploration));
        Assert.True(HudVisibility.ShowsVitals(HudMode.Exploration));
        Assert.True(HudVisibility.ShowsNavigation(HudMode.Exploration));
        Assert.True(HudVisibility.ShowsPrompt(HudMode.Exploration));
        Assert.True(HudVisibility.ShowsCombat(HudMode.Exploration));
    }

    [Fact]
    public void EveryModeIsTotal()
    {
        // A new HudMode member that no group predicate handles would default to hidden and take a
        // widget off screen silently. Enumerating the declared set is the same rule the computed
        // locale keys follow (invariant 26).
        foreach (HudMode mode in Enum.GetValues<HudMode>())
        {
            Assert.Equal(mode == HudMode.Exploration, HudVisibility.ShowsHud(mode));
            Assert.Equal(mode != HudMode.Inactive, HudVisibility.ShowsVitals(mode));
        }
    }

    private static float Radians(int degrees) => degrees * MathF.PI / 180f;

    private static MapPin Pin(string id, float distance, MapTier tier) =>
        new(id, id, new Vector2(distance, 0f), MapCategory.Town, tier);
}
