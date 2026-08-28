using System.Collections.Generic;
using System.Linq;
using Embervale.UI;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Zoom-based marker culling and the category taxonomy behind it (Phase 39.5A). This is the
/// mechanism that keeps the Embermarket's twelve merchants from becoming icon soup, so "a settlement
/// is visible at every zoom" and "a stall is not visible at realm zoom" are properties worth pinning
/// rather than assuming.
/// </summary>
public class MapTierTests
{
    [Fact]
    public void PrimaryIsVisibleAtEveryZoom()
    {
        Assert.True(MapTiers.VisibleAt(MapTier.Primary, MapProjection.MinZoom));
        Assert.True(MapTiers.VisibleAt(MapTier.Primary, MapProjection.MaxZoom));
    }

    [Fact]
    public void DetailIsHiddenAtRealmZoomAndShownWhenZoomedIn()
    {
        Assert.False(MapTiers.VisibleAt(MapTier.Detail, MapProjection.MinZoom));
        Assert.True(MapTiers.VisibleAt(MapTier.Detail, MapTiers.DetailZoom));
        Assert.True(MapTiers.VisibleAt(MapTier.Detail, MapProjection.MaxZoom));
    }

    [Fact]
    public void SecondaryAppearsBeforeDetailDoes()
    {
        Assert.True(MapTiers.SecondaryZoom < MapTiers.DetailZoom);
        Assert.True(MapTiers.VisibleAt(MapTier.Secondary, MapTiers.SecondaryZoom));
        Assert.False(MapTiers.VisibleAt(MapTier.Detail, MapTiers.SecondaryZoom));
    }

    [Fact]
    public void TierFadesInBeforeReachingFullContrastAtRevealZoom()
    {
        float start = MapTiers.DetailZoom - MapTiers.FadeSpan;
        float midway = MapTiers.OpacityAt(
            MapTier.Detail, start + (MapTiers.FadeSpan * 0.5f));

        Assert.Equal(0f, MapTiers.OpacityAt(MapTier.Detail, start - 0.01f));
        Assert.InRange(midway, 0.45f, 0.55f);
        Assert.Equal(1f, MapTiers.OpacityAt(MapTier.Detail, MapTiers.DetailZoom), 3);
        Assert.True(MapTiers.VisibleAt(MapTier.Detail, start));
    }

    [Fact]
    public void RevealZoom_ActuallyRevealsItsOwnTier()
    {
        // Selecting a search result zooms to RevealZoom. If that zoom did not show the tier, the map
        // would centre on an invisible pin and read as broken search.
        foreach (MapTier tier in System.Enum.GetValues<MapTier>())
        {
            Assert.True(
                MapTiers.VisibleAt(tier, MapTiers.RevealZoom(tier)),
                $"{tier} is not visible at its own RevealZoom");
        }
    }

    [Fact]
    public void EveryCategoryBelongsToExactlyOneGroup()
    {
        MapCategory[] all = System.Enum.GetValues<MapCategory>();
        List<MapCategory> grouped = System.Enum.GetValues<MapGroup>()
            .SelectMany(MapCategories.InGroup)
            .ToList();

        Assert.Equal(all.Length, grouped.Count);
        Assert.Equal(all.Length, grouped.Distinct().Count());
    }

    [Fact]
    public void SettlementsAreAlwaysVisibleAndTradeIsNot()
    {
        // The readability contract in one test: you can always see the towns, and you only see the
        // individual shops once you have zoomed into one.
        foreach (MapCategory category in MapCategories.InGroup(MapGroup.Settlement))
        {
            Assert.True(MapTiers.VisibleAt(MapCategories.DefaultTier(category), MapProjection.MinZoom));
        }

        foreach (MapCategory category in MapCategories.InGroup(MapGroup.Trade))
        {
            Assert.False(MapTiers.VisibleAt(MapCategories.DefaultTier(category), MapProjection.MinZoom));
        }
    }

    [Fact]
    public void LocaleKeysAreWellFormed()
    {
        Assert.Equal("map.category.smith", MapCategories.NameKey(MapCategory.Smith));
        Assert.Equal("map.group.settlement", MapCategories.NameKey(MapGroup.Settlement));
    }

    [Fact]
    public void GroupOf_IsTotal()
    {
        // No category may fall through to the default arm, or it lands in Exploration by accident
        // and its filter row silently does nothing.
        foreach (MapCategory category in System.Enum.GetValues<MapCategory>())
        {
            MapGroup group = MapCategories.GroupOf(category);
            Assert.Contains(category, MapCategories.InGroup(group));
        }
    }

    [Fact]
    public void DiscoveryFeedback_AnnouncesMeaningfulApproachButNotBulkKnowledgeOrShops()
    {
        Assert.True(Notifications.ShouldAnnounceDiscovery(false, MapTier.Secondary));
        Assert.False(Notifications.ShouldAnnounceDiscovery(true, MapTier.Secondary));
        Assert.False(Notifications.ShouldAnnounceDiscovery(false, MapTier.Detail));
    }
}
