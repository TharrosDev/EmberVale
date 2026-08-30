using Godot;

namespace Embervale.World;

/// <summary>
/// One cell's window onto the region's ground: how large it is, how finely its terrain is
/// tessellated, and the geography, routes and working surfaces authored on it.
///
/// ⚠️ <b>THE CARDINAL ROAD STRIP IS GONE (the 2026-08-29 geography overhaul).</b> <c>RoadAxis</c>,
/// <c>RoadWidth</c> and <c>RoadOffset</c> painted a straight strip down the middle of a cell; the
/// 2026-08-28 layout rebuild had already set the axis to None on all fifteen cells because no
/// location's circulation ran through its own centre, which left three fields the shader still read
/// and nobody could tune. Routes are <see cref="Paths"/> and nothing else now.
///
/// ⚠️ <b>THIS RESOURCE NO LONGER DESCRIBES A SKIN OVER A FLAT SLAB.</b> The generated terrain is the
/// cell's collision and its navigation source; the 0.5 m <c>BoxMesh</c> floors are deleted from the
/// scenes. Author <see cref="Landforms"/> first, then <see cref="Paths"/>, then
/// <see cref="GroundAreas"/> for the pads the buildings stand on.
/// </summary>
[GlobalClass]
public partial class WorldCellPresentationResource : Resource
{
    [Export(PropertyHint.Range, "8,400,1")] public float Width { get; set; } = 52f;
    [Export(PropertyHint.Range, "8,400,1")] public float Depth { get; set; } = 52f;
    [Export] public int Seed { get; set; } = 38;
    [Export] public Color Tint { get; set; } = Colors.White;
    [Export(PropertyHint.Range, "0,1,0.05")] public float TintStrength { get; set; }

    /// <summary>
    /// Terrain grid divisions across the cell. Aim for a vertex every 1.5–2 m where the player walks
    /// a shaped landform and every 3–5 m across open transitional country — a large quiet cell should
    /// cost no more vertices than a small busy one.
    /// </summary>
    [Export(PropertyHint.Range, "4,160,1")] public int TopologyResolution { get; set; } = 48;

    /// <summary>Multiplier on the region's noise relief for this cell only. Landforms are unaffected.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float TopologyHeightScale { get; set; } = 1f;

    /// <summary>
    /// The cell's geography: hills, ridges, cliffs, cuts, terraces, basins and passes, in cell-local
    /// metres. These may deliberately overhang the envelope — see <see cref="WorldLandformResource"/>.
    /// </summary>
    [Export] public Godot.Collections.Array<WorldLandformResource> Landforms { get; set; } = new();

    /// <summary>Authored approach and circulation network in cell-local X/Z metres.</summary>
    [Export] public Godot.Collections.Array<WorldPathSegmentResource> Paths { get; set; } = new();

    /// <summary>Authored plazas, work yards, building pads, pit floors and landmark clearings.</summary>
    [Export] public Godot.Collections.Array<WorldGroundAreaResource> GroundAreas { get; set; } = new();
}
