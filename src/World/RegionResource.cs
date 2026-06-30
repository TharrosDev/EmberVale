using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored region of the world (Phase 25): a named area within a <see cref="Realm"/>,
/// composed of one or more streamable sub-cells, with nominal bounds, an atmosphere bias, and links
/// to neighbouring regions (the seed of the map + fast-travel graph). Authored as a <c>.tres</c>
/// under <c>data/regions/</c> and indexed by <see cref="RegionDatabase"/> — a new region is a new
/// resource, no code.
///
/// The <see cref="RegionStreamer"/> (25B) reads <see cref="Cells"/> to load/unload scenes by
/// distance, and the map/fast-travel work (25E–25G) reads <see cref="Neighbours"/> and the
/// discovery graph.
/// </summary>
[GlobalClass]
public partial class RegionResource : Resource
{
    /// <summary>Stable id, e.g. "region.ember_crown". The save header + database key.</summary>
    [Export] public string Id { get; set; } = "region.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Region";

    /// <summary>The realm this region belongs to (the lore taxonomy it rolls up under).</summary>
    [Export] public Realm Realm { get; set; } = Realm.EmberCrown;

    /// <summary>Where the player appears when entering this region via a hard transition (Phase 25C).
    /// The transition handler teleports the player here; neighbour portals are placed relative to it.</summary>
    [Export] public Vector3 SpawnPoint { get; set; } = Vector3.Zero;

    /// <summary>The streamable sub-cells that make up this region. The <see cref="RegionStreamer"/>
    /// loads/unloads these by distance (Phase 25B). The procedural sandbox keeps an always-loaded
    /// base and lists its peripheral cells here.</summary>
    [Export] public Godot.Collections.Array<RegionCellResource> Cells { get; set; } = new();

    /// <summary>Nominal world-space extents of the region, for the streamer's distance budget and the
    /// map. The flat sandbox uses a generous box (its floor is an infinite plane).</summary>
    [Export] public Aabb Bounds { get; set; } = new(new Vector3(-256f, -16f, -256f), new Vector3(512f, 64f, 512f));

    [ExportGroup("Atmosphere bias")]
    /// <summary>The weather state this region favours/starts in (a <c>weather.*</c> id).</summary>
    [Export] public string DefaultWeatherId { get; set; } = "weather.clear";

    /// <summary>The day phase that best characterises the region (a mood hint; not a hard lock).</summary>
    [Export] public DayPhase DayPhaseBias { get; set; } = DayPhase.Day;

    [ExportGroup("Safe zone")]
    /// <summary>Centre (world space) of the region's single safe bubble — its town — where the ambient
    /// spawners keep enemies and hostile events out (Phase 27D follow-up). <see cref="SafeZoneRadius"/>
    /// 0 = no safe zone. Scripted spawns bypass it; see <see cref="SafeZones"/>.</summary>
    [Export] public Vector3 SafeZoneCenter { get; set; } = Vector3.Zero;

    [Export] public float SafeZoneRadius { get; set; }

    [ExportGroup("Magic")]
    /// <summary>The strength of the fading <b>Weave</b> in this region (Phase 29.5E), in [0,1]:
    /// 1 = magic flows full, lower = the Weave is failing here (ordinary casts weaken and cost more,
    /// corrupted casts grow stronger and cheaper). Dev-tunable mood dial; see <see cref="Weave"/>.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float WeavePotency { get; set; } = 1f;

    [ExportGroup("Graph")]
    /// <summary>Ids of directly-reachable neighbouring regions (the map + fast-travel adjacency).</summary>
    [Export] public Godot.Collections.Array<string> Neighbours { get; set; } = new();
}
