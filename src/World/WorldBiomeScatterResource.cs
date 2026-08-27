using Godot;

namespace Embervale.World;

/// <summary>Designer-authored deterministic cosmetic ecology for one world cell.</summary>
[GlobalClass]
public partial class WorldBiomeScatterResource : Resource
{
    [Export] public int Seed { get; set; } = 3801;
    [Export(PropertyHint.Range, "0,24,0.5")] public float EdgePadding { get; set; } = 3f;
    [Export] public Godot.Collections.Array<BiomeScatterLayerResource> Layers { get; set; } = new();
    [Export] public Godot.Collections.Array<BiomeScatterExclusionResource> Exclusions { get; set; } = new();
}
