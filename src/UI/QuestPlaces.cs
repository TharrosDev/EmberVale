using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Name and region lookups shared by the journal, the HUD tracker, the compass and the map, so every quest
/// surface names an objective's place by the same rule (<see cref="ObjectiveNavigation.LocationId"/>) and
/// resolves its region the same way. Reads the static databases only.
/// </summary>
public static class QuestPlaces
{
    /// <summary>The map location id an objective points at: Reach and Defend name a place in
    /// <c>TargetId</c>; every other type in <c>LocationId</c>. Empty when it has none.</summary>
    public static string PlaceId(ObjectiveResource objective) =>
        ObjectiveNavigation.LocationId(objective.Type, objective.TargetId, objective.LocationId);

    /// <summary>The player-facing name of an objective's place, or null when it has none or the location is unknown.</summary>
    public static string? PlaceName(ObjectiveResource objective)
    {
        string id = PlaceId(objective);
        return id.Length > 0 && MapLocationDatabase.Get(id) is { } location ? Loc.T(location.NameKey) : null;
    }

    /// <summary>The region that owns a location's cell, or null when unknown.</summary>
    public static RegionResource? RegionOfLocation(string locationId)
    {
        if (locationId.Length == 0 || MapLocationDatabase.Get(locationId) is not { CellId.Length: > 0 } location)
        {
            return null;
        }

        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == location.CellId)
                {
                    return region;
                }
            }
        }

        return null;
    }

    public static string RegionIdOfLocation(string locationId) => RegionOfLocation(locationId)?.Id ?? string.Empty;

    /// <summary>A region's display name for a chip or label, or empty when the id is unknown.</summary>
    public static string RegionName(string regionId) =>
        regionId.Length > 0 && RegionDatabase.Get(regionId) is { } region ? region.DisplayName : string.Empty;

    /// <summary>The region graph: each region's neighbours, as authored.</summary>
    public static Dictionary<string, IReadOnlyList<string>> RegionGraph()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>();
        foreach (RegionResource region in RegionDatabase.All)
        {
            graph[region.Id] = new List<string>(region.Neighbours);
        }

        return graph;
    }

    /// <summary>Whether the player may enter a region: it has no unlock flag, or the player holds it.</summary>
    public static bool IsRegionOpen(string regionId, IEntity? player)
    {
        if (RegionDatabase.Get(regionId) is not { } region || region.UnlockFlagId.Length == 0)
        {
            return true;
        }

        return player?.GetComponent<StoryFlagsComponent>()?.Has(region.UnlockFlagId) == true;
    }

    /// <summary>Where the door from <paramref name="fromRegionId"/> to its neighbour stands, mirroring where
    /// <c>RegionSetup.RebuildPortals</c> places it (an authored door point, else a few metres in front of the
    /// spawn). Null when the region is unknown.</summary>
    public static Vector3? PortalPosition(string fromRegionId, string toRegionId)
    {
        if (RegionDatabase.Get(fromRegionId) is not { } region)
        {
            return null;
        }

        Vector3 door = region.PortalPointFor(toRegionId);
        return WorldGround.OnGround(door != Vector3.Zero ? door : region.SpawnPoint + new Vector3(0f, -1.2f, -4f));
    }

    public static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    public static string ActiveRegionId() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer)
            ? streamer.ActiveRegionId
            : string.Empty;
}
