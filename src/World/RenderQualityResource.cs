using Godot;

namespace Embervale.World;

/// <summary>Cost controls independent of the world's palette and lighting curves.</summary>
[GlobalClass]
public partial class RenderQualityResource : Resource
{
    [Export] public float RenderScale { get; set; } = 1f;
    [Export] public float MeshLodThreshold { get; set; } = 1f;
    [Export] public string Id { get; set; } = "medium";
    [Export] public int ShadowAtlasSize { get; set; } = 2048;
    [Export] public float ShadowDistance { get; set; } = 90f;
    [Export] public bool AmbientOcclusion { get; set; } = true;
    [Export] public bool IndirectLighting { get; set; }
    [Export] public bool VolumetricFog { get; set; }
    [Export] public bool Reflections { get; set; }
    [Export] public float ParticleScale { get; set; } = 0.5f;
    [Export] public int LocalShadowLights { get; set; }
    [Export] public float LocalLightDistance { get; set; } = 45f;
}
