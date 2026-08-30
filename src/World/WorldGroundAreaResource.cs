using Godot;

namespace Embervale.World;

/// <summary>
/// A cell-local activity surface: plaza, work yard, lair bowl, ruin court, pit floor, building pad
/// or gathering clearing. It levels the ground beneath itself and keeps vegetation off it.
///
/// ⚠️ <b>SINCE THE 2026-08-29 OVERHAUL IT IS THE GROUND, NOT A TINT.</b> Terrain now carries real
/// elevation and real collision, so an area is what makes a settlement, a yard or a pit floor flat
/// enough to author buildings on. Every building cluster wants one; a structure on raw hillside
/// will lean into it.
/// </summary>
[GlobalClass]
public partial class WorldGroundAreaResource : Resource
{
    [Export] public Vector2 Center { get; set; } = Vector2.Zero;
    [Export] public Vector2 Radius { get; set; } = new(6f, 6f);
    [Export(PropertyHint.Range, "0,8,0.25")] public float Feather { get; set; } = 2f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float SurfaceBlend { get; set; } = 0.55f;

    /// <summary>
    /// The world Y this surface levels to, in metres. Authored ABSOLUTE (not relative to the cell),
    /// because a yard, a terrace and a pit floor are places in the world that props and colliders
    /// are built against — a value that moved when the cell moved would be the 37C placement bug in
    /// yet another hat. Leave 0 for ground at sea level.
    /// </summary>
    [Export(PropertyHint.Range, "-60,120,0.25")] public float Elevation { get; set; }
}
