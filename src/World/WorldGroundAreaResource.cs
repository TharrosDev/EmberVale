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
    /// The world Y this surface levels to, in metres, or the offset from the generated ground under
    /// its own centre — see <see cref="ElevationMode"/>. Never relative to the CELL: a yard, a
    /// terrace and a pit floor are places in the world that props and colliders are built against,
    /// and a value that moved when the cell moved would be the 37C placement bug in another hat.
    /// </summary>
    [Export(PropertyHint.Range, "-60,200,0.25")] public float Elevation { get; set; }

    /// <summary>
    /// How <see cref="Elevation"/> is read. <b>0 Absolute</b> — a world Y, fixed forever.
    /// <b>1 RelativeToBase</b> — metres above the GENERATED ground at this area's own centre,
    /// resolved once per region load in <see cref="WorldTerrainMeshBuilder.HeightfieldFor"/>.
    ///
    /// ⚠️ <b>RELATIVE IS THE RIGHT ANSWER FOR ALMOST EVERY AUTHORED PAD AND IT IS WHY THE REALM
    /// SURVIVED GETTING REAL GEOGRAPHY.</b> Absolute elevations were authored against a field that
    /// was two octaves of noise and never more than a metre and a half from zero; the moment the
    /// generator put a hillside under a settlement, every one of those pads became a step with a
    /// cliff on its uphill side. A relative pad is still perfectly flat and still absolutely placed
    /// at load — it simply follows the country the generator put it in, so re-tuning a region
    /// profile never needs a re-anchoring pass again. Use Absolute only where a specific world Y is
    /// the point: a waterline shelf, a mine floor cut to a fixed depth, an interior threshold.
    /// </summary>
    [Export(PropertyHint.Enum, "Absolute:0,RelativeToBase:1")]
    public int ElevationMode { get; set; }
}
