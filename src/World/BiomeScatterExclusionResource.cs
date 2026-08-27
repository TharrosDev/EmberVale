using Godot;

namespace Embervale.World;

/// <summary>A cell-local circular clearing around a landmark, route, arena, or authored prop cluster.</summary>
[GlobalClass]
public partial class BiomeScatterExclusionResource : Resource
{
    [Export] public Vector2 Center { get; set; } = Vector2.Zero;
    [Export(PropertyHint.Range, "0,128,0.5")] public float Radius { get; set; } = 5f;
}
