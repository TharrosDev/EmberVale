using Godot;

namespace Embervale.World;

/// <summary>
/// One streamable sub-cell of a <see cref="RegionResource"/> (Phase 25B): a scene that the
/// <see cref="RegionStreamer"/> instances when the player comes within <see cref="LoadRadius"/> of
/// <see cref="Center"/> and frees when they leave (plus a hysteresis margin). The lightweight
/// metadata here lets the streamer decide *whether* to load without instancing the heavy scene.
/// Authored as a sub-resource inside the region's <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class RegionCellResource : Resource
{
    /// <summary>Stable id, <c>&lt;region&gt;.&lt;cell&gt;</c> (e.g. "ember_crown.waystone").</summary>
    [Export] public string Id { get; set; } = "region.cell";

    /// <summary>The cell scene to instance, by the §2.6h-2 convention
    /// <c>res://scenes/regions/&lt;region&gt;/&lt;cell&gt;.tscn</c>.</summary>
    [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";

    /// <summary>World-space centre the cell loads around (the instance is placed here).</summary>
    [Export] public Vector3 Center { get; set; } = Vector3.Zero;

    /// <summary>Planar distance (metres) within which the cell is loaded.</summary>
    [Export] public float LoadRadius { get; set; } = 32f;
}
