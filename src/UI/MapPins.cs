using System.Collections.Generic;
using Embervale.Localization;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Builds the <see cref="MapPin"/> list every map surface draws (39.5B).
///
/// ⚠️ <b>There is exactly one of these on purpose.</b> The full map screen and the HUD minimap are
/// two views of the same discovery state, and the moment each builds its own pins they can disagree
/// about what the player has found, what a place is called, or which tier it belongs to — which
/// reads to the player as one of the two being broken rather than as a drift. Invariant 5 says the
/// map, the minimap and the compass must reference the same destinations; this is where that starts.
///
/// Lifted verbatim out of <c>MapScreen.RebuildPins</c>, which was its only implementation.
/// </summary>
public static class MapPins
{
    /// <summary>Every location the player has discovered, as drawable pins. Empty when the map
    /// service is unavailable — a map with no pins, never a crash (39.5A's "a nameless map is a
    /// degraded map, a crashed one is no map").</summary>
    public static void Rebuild(List<MapPin> into, MapService? map)
    {
        into.Clear();
        if (map == null)
        {
            return;
        }

        foreach (MapLocationView view in map.DiscoveredLocations())
        {
            into.Add(new MapPin(
                view.Location.Id,
                Loc.T(view.Location.NameKey),
                new Vector2(view.Position.X, view.Position.Z),
                view.Location.Category,
                view.Location.EffectiveTier));
        }
    }
}
