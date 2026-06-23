using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored region of the world (Phase 25): a named area within a <see cref="Realm"/>,
/// composed of one or more streamable sub-cells, with nominal bounds, an atmosphere bias, and links
/// to neighbouring regions (the seed of the map + fast-travel graph). Authored as a <c>.tres</c>
/// under <c>data/regions/</c> and indexed by <see cref="RegionDatabase"/> — a new region is a new
/// resource, no code.
///
/// 25A only defines and indexes regions (the sandbox is one); the <c>RegionStreamer</c> (25B) reads
/// <see cref="SubCells"/> to load/unload scenes by distance, and the map/fast-travel work (25E–25G)
/// reads <see cref="Neighbours"/> and the discovery graph.
/// </summary>
[GlobalClass]
public partial class RegionResource : Resource
{
    /// <summary>Stable id, e.g. "region.ember_crown". The save header + database key.</summary>
    [Export] public string Id { get; set; } = "region.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Region";

    /// <summary>The realm this region belongs to (the lore taxonomy it rolls up under).</summary>
    [Export] public Realm Realm { get; set; } = Realm.EmberCrown;

    /// <summary>Ids of the streamable sub-cell scenes that make up this region. The 25B streamer
    /// loads/unloads these by distance; a small region may have just one (the sandbox does).</summary>
    [Export] public Godot.Collections.Array<string> SubCells { get; set; } = new();

    /// <summary>Nominal world-space extents of the region, for the streamer's distance budget and the
    /// map. The flat sandbox uses a generous box (its floor is an infinite plane).</summary>
    [Export] public Aabb Bounds { get; set; } = new(new Vector3(-256f, -16f, -256f), new Vector3(512f, 64f, 512f));

    [ExportGroup("Atmosphere bias")]
    /// <summary>The weather state this region favours/starts in (a <c>weather.*</c> id).</summary>
    [Export] public string DefaultWeatherId { get; set; } = "weather.clear";

    /// <summary>The day phase that best characterises the region (a mood hint; not a hard lock).</summary>
    [Export] public DayPhase DayPhaseBias { get; set; } = DayPhase.Day;

    [ExportGroup("Graph")]
    /// <summary>Ids of directly-reachable neighbouring regions (the map + fast-travel adjacency).</summary>
    [Export] public Godot.Collections.Array<string> Neighbours { get; set; } = new();
}
