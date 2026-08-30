using Godot;

namespace Embervale.World;

/// <summary>
/// One stylised ground material — soil, packed dirt, mud, grass, dead grass, moss, stone, cliff
/// rock, gravel, scree, ash, burnt soil, snow, ice, wet shore, marsh or mine spoil — as the terrain
/// shader consumes it.
///
/// ⚠️ <b>THERE ARE NO TEXTURE FILES HERE AND THAT IS THE CONTRACT, NOT A SHORTCUT.</b>
/// <c>docs/ART_STYLE.md</c> §4/§6.3 forbid photo texturing outright ("a photo-realistic texture would
/// still be wrong here"), and the whole in-house model set ships with zero texture images bound to
/// hand-named palette materials. A downloaded CC0 PBR ground set would read as a different game
/// under the Quaternius props standing on it. So a layer is a <b>two-tone painted surface</b>: two
/// palette colours, the scale the paint varies at, how hard the two tones separate, and how much
/// micro-relief the shader derives into the normal. The variety comes from the noise field and the
/// blending rules in <see cref="WorldBiomeProfileResource"/>, which is exactly the "detail lives in
/// the material pass" line of §1.1 — and it costs no VRAM, no import, and no anti-tiling work,
/// because there is no tile.
///
/// A layer is shared: the same <c>terrain.rock_grey</c> is the cliff of five biomes. Author them in
/// <c>data/terrain_layers/</c> and reference them from a biome profile.
/// </summary>
[GlobalClass]
public partial class WorldTerrainLayerResource : Resource
{
    /// <summary>Authoring label — what this surface is. Never shown to a player.</summary>
    [Export] public string Id { get; set; } = "terrain.layer";

    /// <summary>The shaded-down tone. Keep saturation under ~40% (ART_STYLE §2).</summary>
    [Export] public Color Low { get; set; } = new(0.24f, 0.20f, 0.15f);

    /// <summary>The lifted tone the breakup noise mixes toward.</summary>
    [Export] public Color High { get; set; } = new(0.34f, 0.29f, 0.20f);

    /// <summary>Metres per cycle of this layer's own paint variation. Small = fine grain.</summary>
    [Export(PropertyHint.Range, "0.4,40,0.1")] public float Grain { get; set; } = 4f;

    /// <summary>0 keeps one flat tone; 1 swings the full way between <see cref="Low"/> and <see cref="High"/>.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Breakup { get; set; } = 0.7f;

    /// <summary>Micro-relief the shader derives into the normal. 0 is glass-flat; 1 is coarse rubble.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Relief { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set; } = 0.92f;

    /// <summary>Ice and wet stone want a little specular; soil and snow do not.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Specular { get; set; } = 0.4f;
}
