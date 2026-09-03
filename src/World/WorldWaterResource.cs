using Godot;

namespace Embervale.World;

/// <summary>
/// One body of standing water on a cell — a tarn, a flooded street, a sump, a meltwater pool — in
/// cell-local X/Z metres, at an absolute world surface height.
///
/// ⚠️ <b>WATER IS DATA NOW, NOT A BOX IN A SCENE, AND THAT IS A SAFETY CONTRACT RATHER THAN A
/// TIDY-UP.</b> Every water surface in the realm used to be a <c>BoxMesh</c> in the cell's <c>.tscn</c>
/// with a translucent material, no collider, and nothing in the game that knew it existed. Two
/// consequences followed and both shipped: the rendered rectangle did not match the elliptical basin
/// carved under it, so open water lay on dry land at its corners; and because no system could answer
/// "is the player in water", a player who walked off Hollowreach's 53-degree drop-off landed in a
/// 4.5 m basin <b>in a game with no swimming</b> and had no way out. Declaring the body here fixes
/// both: <see cref="WorldCellWater"/> draws it with a real shoreline taken from the terrain, and
/// <see cref="WorldWater"/> lets <see cref="WorldWaterSafety"/> honour the recovery contract.
///
/// ⚠️ <b><see cref="SurfaceY"/> IS ABSOLUTE, LIKE <see cref="WorldGroundAreaResource.Elevation"/>.</b>
/// A waterline is a place in the world that banks, jetties and boats are built against; a value
/// that moved when its cell moved would be the 37C placement bug in yet another hat.
/// </summary>
[GlobalClass]
public partial class WorldWaterResource : Resource
{
    /// <summary>Authoring label. Never shown to a player.</summary>
    [Export] public string Id { get; set; } = "water";

    /// <summary>Centre of the surface rectangle, in cell-local metres.</summary>
    [Export] public Vector2 Center { get; set; } = Vector2.Zero;

    /// <summary>
    /// Half-width and half-depth of the surface rectangle.
    ///
    /// ⚠️ <b>DRAW IT GENEROUSLY LARGER THAN THE WET GROUND.</b> The shoreline is not this rectangle —
    /// it is wherever the terrain rises through <see cref="SurfaceY"/>, and the mesh fades out
    /// there. An extent that stops short leaves a visible straight edge of water in the middle of a
    /// basin; one that overhangs the bank simply renders nothing over the dry part.
    /// </summary>
    [Export] public Vector2 Extent { get; set; } = new(20f, 20f);

    /// <summary>Absolute world Y of the surface.</summary>
    [Export(PropertyHint.Range, "-60,120,0.05")] public float SurfaceY { get; set; }

    [Export] public Color ShallowColor { get; set; } = new(0.22f, 0.34f, 0.33f);
    [Export] public Color DeepColor { get; set; } = new(0.05f, 0.13f, 0.17f);

    /// <summary>Depth in metres at which the water reads fully opaque.</summary>
    [Export(PropertyHint.Range, "0.2,12,0.1")] public float OpaqueDepth { get; set; } = 2.2f;

    /// <summary>
    /// How <see cref="SurfaceY"/> is read. <b>0 Absolute</b> — a world Y. <b>1 RelativeToBase</b> —
    /// metres above the GENERATED ground under this body's own centre, resolved once per region load.
    ///
    /// ⚠️ <b>THIS IS THE THIRD TIME THE SAME BUG HAS BEEN FOUND IN THIS PASS AND IT WAS BY FAR THE
    /// WORST.</b> A ground area's elevation, a levelling landform's height and a waterline are all
    /// targets stated as an absolute world Y, and all three were authored against a field that never
    /// left the range -1.5..1.5. Give the realm real geography and each of them stops meaning what
    /// it said — but a pad becomes a step and a shelf becomes a climb, whereas a waterline becomes a
    /// FLOOD. The generated ground under Hollowreach sits at about -8.4 m; its fen was authored at a
    /// surface of 0.05, so the same rectangle that used to be a wharf-side channel swallowed its own
    /// shoreline, the whole basin, and the neighbouring Fen Edge cell with it.
    ///
    /// As an offset the waterline follows its basin: the floor is dug by landforms that also follow
    /// the country, so the depth an author chose is the depth they keep.
    /// </summary>
    [Export(PropertyHint.Enum, "Absolute:0,RelativeToBase:1")]
    public int ElevationMode { get; set; }

    /// <summary>
    /// This body's waterline in world Y, resolving <see cref="ElevationMode"/> against the generated
    /// ground beneath its centre.
    ///
    /// ⚠️ It reads <see cref="WorldHeightfield.GeneratedElevation"/>, never <c>Height</c>: a lake
    /// levelled against the landforms in its own basin would sit on the bottom of the hole it is
    /// supposed to fill.
    /// </summary>
    public float ResolveSurface(WorldHeightfield? field, Vector3 cellCenter) =>
        ElevationMode == 1 && field != null
            ? field.GeneratedElevation(cellCenter.X + Center.X, cellCenter.Z + Center.Y) + SurfaceY
            : SurfaceY;
}
