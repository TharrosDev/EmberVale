using Godot;

namespace Embervale.World;

/// <summary>One batched species/layer in a cell's deterministic biome dressing.</summary>
[GlobalClass]
public partial class BiomeScatterLayerResource : Resource
{
    [Export(PropertyHint.File, "*.glb,*.gltf,*.tscn")] public string ScenePath { get; set; } = string.Empty;
    [Export(PropertyHint.Range, "0,512,1")] public int Count { get; set; } = 24;
    [Export(PropertyHint.Range, "0.1,4,0.05")] public float MinimumScale { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0.1,4,0.05")] public float MaximumScale { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0,20,0.25")] public float MinimumSpacing { get; set; } = 2f;
    [Export] public Color Tint { get; set; } = Colors.White;
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float TintVariation { get; set; } = 0.08f;
    [Export(PropertyHint.Range, "10,500,5")] public float VisibilityRangeEnd { get; set; } = 140f;
    [Export] public bool CastShadows { get; set; } = true;
}
