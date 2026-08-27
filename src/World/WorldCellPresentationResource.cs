using Godot;

namespace Embervale.World;

/// <summary>Per-cell authored surface envelope layered over the proven flat gameplay collider.</summary>
[GlobalClass]
public partial class WorldCellPresentationResource : Resource
{
    [Export(PropertyHint.Range, "8,256,1")] public float Width { get; set; } = 52f;
    [Export(PropertyHint.Range, "8,256,1")] public float Depth { get; set; } = 52f;
    [Export(PropertyHint.Enum, "None,NorthSouth,EastWest")] public int RoadAxis { get; set; }
    [Export(PropertyHint.Range, "1,16,0.5")] public float RoadWidth { get; set; } = 5f;
    [Export(PropertyHint.Range, "-64,64,0.5")] public float RoadOffset { get; set; }
    [Export] public int Seed { get; set; } = 38;
    [Export] public Color Tint { get; set; } = Colors.White;
    [Export(PropertyHint.Range, "0,1,0.05")] public float TintStrength { get; set; }
}
