using Embervale.Core;
using Embervale.Settings;
using Godot;

namespace Embervale.World;

/// <summary>
/// Cost controls independent of the world's palette and lighting curves.
///
/// ⚠️ Every default below is what the game did before the field existed, so a tier file that does
/// not mention a field renders exactly as it always has. Only the tiers under Medium author them.
/// </summary>
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

    // --- World scale: consumed by the streamer, published from SkyController.ApplyQuality ---------

    /// <summary>Multiplier on streaming and visibility distances. 1 = the region's authored budget.</summary>
    [Export(PropertyHint.Range, "0.3,1,0.05")] public float DrawDistanceScale { get; set; } = 1f;

    /// <summary>Multiplier on biome scatter instance counts. 1 = the authored density.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float ScatterDensityScale { get; set; } = 1f;

    /// <summary>Metres from the camera beyond which characters cast no shadow. The default is far
    /// past any shadow distance, which is "never", as before.</summary>
    [Export] public float ActorShadowDistance { get; set; } = 10000f;

    // --- Sun shadow -------------------------------------------------------------------------------

    /// <summary>Directional cascades: 1, 2 or 4. Each one is a full extra pass over the shadow casters.</summary>
    [Export(PropertyHint.Enum, "One:1,Two:2,Four:4")] public int ShadowSplits { get; set; } = 4;

    /// <summary>Cross-fades neighbouring cascades, which samples two of them across each seam.</summary>
    [Export] public bool ShadowBlendSplits { get; set; } = true;

    /// <summary>Soft-shadow filter taps, as <c>RenderingServer.ShadowQuality</c> (0 hard … 5 ultra).
    /// -1 keeps the project setting.</summary>
    [Export(PropertyHint.Range, "-1,5,1")] public int ShadowFilterQuality { get; set; } = -1;

    /// <summary>Omni/spot shadow atlas edge in pixels. 0 keeps the project setting and its quadrants.</summary>
    [Export] public int PositionalShadowAtlasSize { get; set; }

    // --- Post and sky -----------------------------------------------------------------------------

    [Export] public bool Glow { get; set; } = true;

    /// <summary>Bicubic glow upscale. Off is one bilinear tap per level instead of sixteen.</summary>
    [Export] public bool GlowBicubicUpscale { get; set; } = true;

    /// <summary>0 off, 1 FXAA, 2 MSAA 2x, 3 MSAA 4x, 4 TAA.</summary>
    [Export(PropertyHint.Enum, "Off,FXAA,MSAA 2x,MSAA 4x,TAA")] public int AntiAliasing { get; set; }

    /// <summary>0 bilinear, 1 FSR 1.0, 2 FSR 2.2. Only matters below a render scale of 1.</summary>
    [Export(PropertyHint.Enum, "Bilinear,FSR 1.0,FSR 2.2")] public int ScalingMode { get; set; }

    /// <summary>Sky radiance cubemap edge: 32, 64, 128 or 256. The sun moves every frame, so this map
    /// is re-rendered and re-filtered continuously and its cost scales with the square of this.</summary>
    [Export(PropertyHint.Enum, "32:32,64:64,128:128,256:256")] public int SkyRadianceSize { get; set; } = 256;

    /// <summary>The preset for a saved <c>Settings.RenderQuality</c> value. Null only if the file is missing.</summary>
    public static RenderQualityResource? ForTier(int tier) =>
        ResidentResources.Load<RenderQualityResource>($"res://data/rendering/{GraphicsMath.TierName(tier)}.tres");
}
