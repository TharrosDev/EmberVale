using Godot;

namespace Embervale.World;

/// <summary>
/// Region-wide visual language for authored world cells. A region owns one profile and its cells
/// own only layout overrides, so a future realm changes palette and material response in data
/// without growing another terrain renderer.
/// </summary>
[GlobalClass]
public partial class WorldEnvironmentProfileResource : Resource
{
    [Export] public Color SurfaceColor { get; set; } = new(0.28f, 0.25f, 0.20f);
    [Export] public Color SecondaryColor { get; set; } = new(0.38f, 0.34f, 0.25f);
    [Export] public Color DetailColor { get; set; } = new(0.15f, 0.14f, 0.12f);
    [Export] public Color RoadColor { get; set; } = new(0.34f, 0.29f, 0.21f);
    [Export] public Color BackdropColor { get; set; } = new(0.22f, 0.20f, 0.18f);
    [Export(PropertyHint.Range, "0,0.18,0.005")] public float Relief { get; set; } = 0.055f;
    [Export(PropertyHint.Range, "0.25,8,0.25")] public float DetailScale { get; set; } = 2.5f;
    [Export] public Vector3 BackdropCenter { get; set; } = Vector3.Zero;
    [Export(PropertyHint.Range, "100,600,5")] public float BackdropRadius { get; set; } = 240f;
    [Export(PropertyHint.Range, "20,160,1")] public float BackdropHeight { get; set; } = 65f;
    [Export(PropertyHint.Range, "8,40,1")] public int BackdropCount { get; set; } = 20;
}
