using Godot;

namespace Embervale.World;

/// <summary>
/// A cell-local activity surface: plaza, work yard, lair bowl, ruin court, or gathering clearing.
/// It softens terrain and vegetation without introducing collision or a second terrain authority.
/// </summary>
[GlobalClass]
public partial class WorldGroundAreaResource : Resource
{
    [Export] public Vector2 Center { get; set; } = Vector2.Zero;
    [Export] public Vector2 Radius { get; set; } = new(6f, 6f);
    [Export(PropertyHint.Range, "0,8,0.25")] public float Feather { get; set; } = 2f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float SurfaceBlend { get; set; } = 0.55f;
}
