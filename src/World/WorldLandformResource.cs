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
}
