using System.Collections.Generic;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>Builds the map's road layer from the same authored presentation paths that skin the
/// playable terrain. This keeps cartography and traversal topology on one source of truth.</summary>
public static class MapCartography
{
    public static List<MapRoadSegment> Roads(MapService? map)
    {
        var roads = new List<MapRoadSegment>();
        if (map == null)
        {
            return roads;
        }

        foreach ((string cellId, Rect2 _) in map.KnownFootprints())
        {
            if (RegionDatabase.Cell(cellId) is not { Presentation: { } presentation } cell)
            {
                continue;
            }

            var offset = new Vector2(cell.Center.X, cell.Center.Z);
            foreach (WorldPathSegmentResource? path in presentation.Paths)
            {
                if (path != null)
                {
                    roads.Add(new MapRoadSegment(offset + path.Start, offset + path.End, path.Width));
                }
            }
        }

        return roads;
    }
}
