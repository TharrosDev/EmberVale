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
    /// <summary>
    /// The region's default ground identity — six semantic material layers and the slope/height/
    /// water rules that place them. A cell may override it with
    /// <see cref="WorldCellPresentationResource.Biome"/>.
    ///
    /// ⚠️ Leaving it null falls back to the four flat colours below, which is what the whole realm
    /// looked like before <see cref="WorldBiomeProfileResource"/> existed: three tones lerped by one
    /// octave of noise. It is a compatibility path, not a choice — author a biome.
    /// </summary>
    [Export] public WorldBiomeProfileResource? Biome { get; set; }

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
    [Export] public int TerrainSeed { get; set; } = 3800;
    [Export(PropertyHint.Range, "0,1,0.01")] public float SurfaceRoughness { get; set; } = 0.96f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float DetailRoughness { get; set; } = 0.88f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float RoadRoughness { get; set; } = 0.82f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float SlopeBlendStart { get; set; } = 0.08f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float SlopeBlendEnd { get; set; } = 0.32f;
    [Export(PropertyHint.Range, "-2,2,0.01")] public float HeightBlendStart { get; set; } = 0.04f;
    [Export(PropertyHint.Range, "-2,2,0.01")] public float HeightBlendEnd { get; set; } = 0.15f;

    // --- Region atmosphere -------------------------------------------------------------------
    // ⚠️ THIS IS THE "PER-REALM VARIATION GETS LIFTED INTO DATA AT PHASE 44" THAT SkyController HAS
    // BEEN CARRYING A COMMENT ABOUT. Every region shared one sun colour, one haze tint and one fog
    // floor, which is why the Clan Hold's neutral-grey bedrock rendered as warm tan sand: the
    // material was cold and the LIGHT was the Ember Crown's. Palette alone cannot make an alpine
    // region look alpine while a golden-hour sun is on it.
    //
    // All four are MULTIPLIERS or tints ON TOP of the day/night and weather curves, never
    // replacements for them: a region colours the light, it does not own the time of day.

    /// <summary>Multiplies the sun's colour. Cold steel (ART_STYLE §2.1) for an alpine realm;
    /// leave white for the Ember Crown's golden-hour amber.</summary>
    [Export] public Color SunTint { get; set; } = Colors.White;

    /// <summary>Multiplies the sun's energy. Under 1 for a realm under permanent overcast.</summary>
    [Export(PropertyHint.Range, "0.3,1.6,0.01")] public float SunEnergyScale { get; set; } = 1f;

    /// <summary>The colour this region's haze settles toward. Ash for the Ember Crown, blue-white
    /// for Frostfang: this is the single strongest cue for distance and biome in a wide shot.</summary>
    [Export] public Color HazeColor { get; set; } = new(0.70f, 0.66f, 0.60f);

    /// <summary>Multiplies fog density, weather included. Frostfang's air carries more water and
    /// more blown snow than the Ember Crown's; over about 2.5 the far cells stop being readable.</summary>
    [Export(PropertyHint.Range, "0.3,3,0.05")] public float HazeScale { get; set; } = 1f;
}
