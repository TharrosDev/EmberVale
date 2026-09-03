using Godot;

namespace Embervale.World;

/// <summary>
/// One authored piece of geography, in <b>cell-local</b> X/Z metres (the 2026-08-29 geography
/// overhaul). Hills, ridgelines, cliffs, mining cuts, spoil banks, terraces, shelves, basins,
/// crater rims, shore slopes, ravines and mountain passes are all this one resource.
///
/// ⚠️ <b>TERRAIN MAKES THE SHAPE; PROPS ONLY DETAIL IT.</b> A corrie built from scaled rock clusters,
/// a crater built from a ring of boulders and a glacier pass built from alternating ice props are
/// the three defects this resource exists to retire. Author the landform first, then dress it.
///
/// ⚠️ <b>IT MAY DELIBERATELY OVERHANG THE CELL ENVELOPE.</b> Everything else authored on a cell is
/// clamped inside its <see cref="WorldCellPresentationResource.Width"/>/<c>Depth</c>; a landform is
/// not, because a ridge that stops dead at a seam re-draws the rectangle the overhaul removed.
/// <see cref="WorldHeightfield"/> pools every cell's landforms in world space, so one authored here
/// shapes the neighbour's ground too and both cells agree at the shared edge by construction.
///
/// <b>Grade discipline.</b> <c>CharacterBody3D</c>'s floor limit is 45°, so a slope under about 0.7
/// rise-over-run is walkable and one over about 1.0 is an honest, collider-free wall. Use the
/// steep end deliberately (cliff, ice face, pit wall) and keep routes on the shallow end;
/// <c>--validate</c> and <c>tools/world_traversal_probe.gd</c> both measure it.
/// </summary>
[GlobalClass]
public partial class WorldLandformResource : Resource
{
    /// <summary>Radial (0) for hills, hollows, craters and pads; swept (1) for ridges and cuts.</summary>
    [Export(PropertyHint.Enum, "Mound,Ridge")] public int Shape { get; set; }

    /// <summary>Centre of a mound, or the first end of a ridge, in cell-local metres.</summary>
    [Export] public Vector2 Center { get; set; } = Vector2.Zero;

    /// <summary>The far end of a ridge, in cell-local metres. Ignored by a mound.</summary>
    [Export] public Vector2 End { get; set; } = Vector2.Zero;

    /// <summary>Mound: the two elliptical radii. Ridge: X is the half-width; Y is unused.</summary>
    [Export] public Vector2 Extent { get; set; } = new(12f, 12f);

    /// <summary>Yaw of a mound's ellipse, in radians. Ignored by a ridge.</summary>
    [Export(PropertyHint.Range, "-3.15,3.15,0.01")] public float Rotation { get; set; }

    /// <summary>Metres. With <see cref="Flatten"/> 0 this is added; with 1 it is the resulting ground level.</summary>
    [Export(PropertyHint.Range, "-60,120,0.25")] public float Height { get; set; } = 4f;

    /// <summary>Fraction of the extent spent on the transition: 0.9 a soft hill, 0.12 a cliff.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float Falloff { get; set; } = 0.7f;

    /// <summary>0 adds to whatever is there; 1 replaces it (a terrace, a pit floor, a shelf).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float Flatten { get; set; }

    /// <summary>
    /// How far this landform's own boundary is bent out of shape by noise, as a fraction of its
    /// extent. 0 is the exact ellipse or swept capsule; 0.25 is a natural hill; 0.45 is a broken,
    /// lobed ridgeline.
    ///
    /// ⚠️ <b>WITHOUT IT EVERY HILL IN THE REALM IS AN ELLIPSE AND EVERY RIDGE IS A CAPSULE, AND FROM
    /// ANY HEIGHT YOU CAN SEE IT.</b> The mask is a smooth function of a normalised radius, so its
    /// contours are perfect concentric ellipses — which is exactly right for a terrace or a building
    /// pad and exactly wrong for a moor. This warps the radius by a deterministic noise field scaled
    /// to the landform's own size, so the shape keeps its authored place, height and grade while its
    /// EDGE stops being drawable with a compass.
    ///
    /// ⚠️ <b>KEEP IT AT 0 ON ANYTHING LEVELLING (<see cref="Flatten"/> near 1).</b> A pit floor, a
    /// terrace or a market pad is a made thing and should look made; warping one puts a wobble in the
    /// edge of a paved surface. The generator's default follows that rule on its own.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float Irregularity { get; set; }

    /// <summary>
    /// How <c>Height</c> is read when this landform LEVELS ground (<c>Flatten</c> over 0.5).
    /// <b>0 Absolute</b> — a world Y. <b>1 RelativeToBase</b> — metres above the generated ground
    /// under this landform's own centre, resolved once per region load.
    ///
    /// ⚠️ <b>A LEVELLING LANDFORM IS A PAD BY ANOTHER NAME AND IT HAD THE SAME BUG.</b> A terrace,
    /// a shelf, a pit floor and a plateau all REPLACE the ground rather than adding to it, so their
    /// Height is a target, and every one of them in the realm was authored as an absolute world Y
    /// against a field that never left the range -1.5..1.5. The Splintered Shelf is the case that
    /// caught it: authored as "a twelve-metre platform, the steepest walkable ground in the realm on
    /// purpose", it sat at a fixed 10 m while the generator put the country around it at -6.6, so
    /// the shelf path climbed sixteen and a half metres over eighteen instead of twelve. Everything
    /// downstream reported that honestly and none of it pointed here: the route grade validator saw
    /// 0.79 and passed it, and the traversal probe snagged a real capsule on ground the author had
    /// deliberately built at the edge of walkable.
    ///
    /// ⚠️ It has no effect on an ADDITIVE landform (<c>Flatten</c> 0) — a hill that adds eight
    /// metres already follows whatever it is sitting on, which is why hills never needed migrating.
    /// </summary>
    [Export(PropertyHint.Enum, "Absolute:0,RelativeToBase:1")]
    public int ElevationMode { get; set; }
}
