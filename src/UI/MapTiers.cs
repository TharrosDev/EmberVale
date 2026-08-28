using Embervale.World;

namespace Embervale.UI;

/// <summary>
/// Which marker tiers are worth drawing at a given zoom (Phase 39.5A) — the brief's §14, and the
/// mechanism that keeps the Embermarket's twelve merchants from becoming icon soup.
///
/// ⚠️ <b>This is why clustering is not in 39.5A.</b> Clustering solves "too many markers in one
/// place at the zoom where they are all visible". Tier culling means that state does not arise:
/// at whole-realm zoom the twelve stalls are not drawn at all, and by the zoom that reveals them
/// the market is forty metres wide on screen and they are metres apart. Clustering lands when two
/// <see cref="MapTier.Detail"/> markers actually overlap at <see cref="DetailZoom"/> — a check
/// someone can run, rather than a feature built on the assumption they will.
///
/// Engine-free, so it is unit-testable.
/// </summary>
public static class MapTiers
{
    /// <summary>Distance in pixels-per-metre over which a newly eligible tier settles to full
    /// opacity. Semantic detail arrives as a controlled transition instead of an icon explosion.</summary>
    public const float FadeSpan = 1.25f;

    /// <summary>Pixels per metre at which regional content (dungeons, mines, gates, waystones)
    /// appears. Below this the map is a realm overview and only settlements are legible.</summary>
    public const float SecondaryZoom = 3.5f;

    /// <summary>Pixels per metre at which individual shops and counter services appear. At 9 px/m a
    /// 40 m market fills 360 px, so its stalls are tens of pixels apart rather than on top of each
    /// other.</summary>
    public const float DetailZoom = 9f;

    /// <summary>True when a tier should be drawn at this zoom.</summary>
    public static bool VisibleAt(MapTier tier, float zoom) =>
        tier == MapTier.Primary || zoom >= RevealZoom(tier) - FadeSpan;

    /// <summary>
    /// The zoom at which a tier first appears — what "zoom in to see shops" needs to know in order
    /// to actually take the player there when they pick a hidden marker out of the search results.
    /// A search result that centres the map on an invisible pin is a bug the player reads as the
    /// search being broken.
    /// </summary>
    public static float RevealZoom(MapTier tier) => tier switch
    {
        MapTier.Primary => MapProjection.MinZoom,
        MapTier.Secondary => SecondaryZoom,
        MapTier.Detail => DetailZoom,
        _ => MapProjection.MinZoom,
    };

    /// <summary>Opacity for a visible tier. The transition begins before the nominal reveal zoom so
    /// search and capture states reach full, readable contrast at that documented threshold.</summary>
    public static float OpacityAt(MapTier tier, float zoom)
    {
        float reveal = RevealZoom(tier);
        if (tier == MapTier.Primary)
        {
            return 1f;
        }

        float progress = System.Math.Clamp((zoom - (reveal - FadeSpan)) / FadeSpan, 0f, 1f);
        return progress * progress * (3f - (2f * progress));
    }
}
