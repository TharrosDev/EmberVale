using Godot;

namespace Embervale.World;

/// <summary>
/// A reusable ground identity — temperate lowland, woodland, wetland, burned heath, excavated,
/// alpine, snowfield, glacier, ash — as six semantic material slots plus the rules that decide
/// which of them the terrain shader shows at a given point.
///
/// ⚠️ <b>A REGION DOES NOT OWN ITS LOOK; IT PICKS ONE.</b> Before this the whole surface response of
/// a realm was four colours and six floats on <see cref="WorldEnvironmentProfileResource"/>, so a
/// third region would have re-derived "what does a cliff look like" from scratch and a fourth would
/// have got it slightly different. A profile lives in <c>data/biomes/</c>, is referenced by any
/// number of regions and cells, and a new region reaches production ground by naming one.
///
/// <b>The six slots are semantic, not a paint order.</b> Each is a place in the world's logic:
/// <list type="bullet">
/// <item><see cref="Ground"/> — what this country is made of where it is flat and dry.</item>
/// <item><see cref="Sparse"/> — the patchy second surface broken into it: dead grass through soil,
/// moss over stone, gravel through ash. Placed by macro noise, never uniformly.</item>
/// <item><see cref="Rock"/> — what a slope exposes. Driven by gradient, so a cliff reads as a cliff
/// with no authoring at all.</item>
/// <item><see cref="Cap"/> — what altitude adds: scree, snow, bare summit stone. Driven by height,
/// and deliberately shed off steep faces (snow does not stick to a wall).</item>
/// <item><see cref="Road"/> — the compacted travelled surface, from the path mask in vertex red.</item>
/// <item><see cref="Shore"/> — the wet margin under and just above the waterline, from
/// <see cref="ShoreLevel"/>. Also what a marsh and a mine sump are made of.</item>
/// </list>
///
/// A cell overrides the region's profile with <see cref="WorldCellPresentationResource.Biome"/> —
/// that is how the Emberdeep's spoil and the Ash Roost's burn differ from the country they sit in,
/// and it replaces the flat per-cell <c>Tint</c> rectangle that did the job before.
/// </summary>
[GlobalClass]
public partial class WorldBiomeProfileResource : Resource
{
    [Export] public string Id { get; set; } = "biome.profile";

    [ExportGroup("Surfaces")]
    [Export] public WorldTerrainLayerResource? Ground { get; set; }
    [Export] public WorldTerrainLayerResource? Sparse { get; set; }
    [Export] public WorldTerrainLayerResource? Rock { get; set; }
    [Export] public WorldTerrainLayerResource? Cap { get; set; }
    [Export] public WorldTerrainLayerResource? Road { get; set; }
    [Export] public WorldTerrainLayerResource? Shore { get; set; }

    /// <summary>Slope (rise over run) where <see cref="Rock"/> starts and finishes taking over.
    /// 0.35..0.85 puts bare rock on anything a player cannot comfortably walk.</summary>
    [ExportGroup("Placement")]
    [Export] public Vector2 SlopeBand { get; set; } = new(0.35f, 0.85f);

    /// <summary>World Y where <see cref="Cap"/> starts and finishes taking over.</summary>
    [Export] public Vector2 HeightBand { get; set; } = new(18f, 34f);

    /// <summary>How hard <see cref="Cap"/> is shed by slope: 0 caps a cliff too, 1 sheds it entirely
    /// off anything past <see cref="SlopeBand"/>. Snow wants ~0.85; scree wants ~0.2.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CapSlopeShed { get; set; } = 0.8f;

    /// <summary>How much of the ground <see cref="Sparse"/> claims, 0..1, before macro noise.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float SparseCoverage { get; set; } = 0.45f;

    /// <summary>Metres per cycle of the macro variation that decides Ground vs Sparse and tints
    /// everything. Big (60–140 m) or the country reads as one repeating fabric.</summary>
    [Export(PropertyHint.Range, "20,400,1")] public float MacroScale { get; set; } = 90f;

    /// <summary>How far the macro field pushes the whole surface toward its two extremes. This is
    /// the anti-tiling term: without it every hundred metres looks like every other hundred.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float MacroStrength { get; set; } = 0.22f;

    /// <summary>World Y of this biome's waterline. <see cref="Shore"/> claims the ground below it and
    /// fades out <see cref="ShoreBand"/> metres above. Leave <see cref="ShoreBand"/> 0 for dry country.</summary>
    [ExportGroup("Water margin")]
    [Export(PropertyHint.Range, "-60,120,0.25")] public float ShoreLevel { get; set; }
    [Export(PropertyHint.Range, "0,12,0.25")] public float ShoreBand { get; set; }

    /// <summary>Metres per bed of the horizontal strata banding painted into <see cref="Rock"/>. This
    /// is what makes a cut face read as bedded stone rather than a tinted ramp.</summary>
    [ExportGroup("Rock structure")]
    [Export(PropertyHint.Range, "0.5,20,0.1")] public float StrataScale { get; set; } = 3.2f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float StrataStrength { get; set; } = 0.35f;
}
