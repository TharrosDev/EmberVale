using Embervale.Core;
using Godot;

namespace Embervale.World;

/// <summary>
/// Where a named place stands, read from the offline world bake rather than from anything a save or a
/// live scene remembers (2026-09 world rebuild).
///
/// ⚠️ <b>WHY THIS EXISTS.</b> A fast-travel landing and a map pin were only ever known while their cell
/// was loaded; otherwise the game used the world position a SAVE recorded. Move a waystone and every
/// existing save kept jumping the player to where it used to be — and re-attuning could not fix it,
/// because <see cref="FastTravelService.Discover"/> ignored a known id. The bake already instances
/// every cell on its final ground, so it writes each <see cref="TravelNodeComponent"/> landing and each
/// <see cref="MapLocationComponent"/> position into the prepared region. That is data derived from the
/// scene the node lives in, never a second authored record, and it is correct for a region the player
/// is not standing in.
/// </summary>
public static class WorldPlaceIndex
{
    /// <summary>Key under which the bake records a travel node's landing point.</summary>
    public static string TravelKey(string nodeId) => "travel:" + nodeId;

    /// <summary>Key under which the bake records a map location's position.</summary>
    public static string LocationKey(string locationId) => "location:" + locationId;

    /// <summary>The landing point of a travel node in <paramref name="regionId"/>, or null: a waystone's
    /// baked landing, or a holding's own build yard (a deed is not a waystone and has no body to bake).</summary>
    public static Vector3? TravelLanding(string regionId, string nodeId)
    {
        if (Of(regionId, TravelKey(nodeId)) is { } baked)
        {
            return baked;
        }
        foreach (Housing.PropertyResource property in Housing.PropertyDatabase.All)
        {
            if (property.TravelNodeId == nodeId && property.PlacementRadius > 0f &&
                RegionDatabase.Cell(property.PlacementCellId) != null)
            {
                return property.PlacementWorldCenter;
            }
        }
        return null;
    }

    /// <summary>The baked position of a map location, found through its cell's region, or null.</summary>
    public static Vector3? Location(string locationId)
    {
        if (MapLocationDatabase.Get(locationId) is not { } location)
        {
            return null;
        }
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource? cell in region.Cells)
            {
                if (cell?.Id == location.CellId)
                {
                    return Of(region.Id, LocationKey(locationId));
                }
            }
        }
        return null;
    }

    private static Vector3? Of(string regionId, string key)
    {
        string path = WorldBakePaths.Region(regionId);
        if (string.IsNullOrEmpty(regionId) || !ResourceLoader.Exists(path) ||
            ResidentResources.Load<WorldPreparedRegionResource>(path) is not { } prepared)
        {
            return null;
        }
        return prepared.Places.TryGetValue(key, out Vector3 position) ? position : null;
    }
}
