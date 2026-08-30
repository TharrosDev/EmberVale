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
    [Export(PropertyHint.Range, "0,100,1")] public float VisibilityFadeMargin { get; set; } = 18f;
    [Export] public bool CastShadows { get; set; } = true;

    /// <summary>
    /// Steepest ground, as rise over run, this species will stand on. 0.7 is the walkable limit and
    /// the default because it is right for nearly everything.
    ///
    /// ⚠️ <b>THIS IS THE FIELD THAT STOPS TREES GROWING OUT OF CLIFFS.</b> Before the terrain carried
    /// real elevation the planner only had to avoid roads and yards, because there was no slope to
    /// avoid; afterwards it kept scattering uniformly over ground that had become a 60-degree face,
    /// and the corrie walls of the Western Wilds, the glacier's rock buttresses and every cut in the
    /// Emberdeep grew a full density of vegetation and boulders sideways out of them. Trees want
    /// about 0.4, scrub 0.6, loose stone 0.95 (scree genuinely does lie on steep ground), and a
    /// species that may go anywhere sets 0.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float MaxSlope { get; set; } = 0.7f;

    /// <summary>
    /// World Y band this species survives in. The default spans the realm, so a layer opts in.
    /// This is the tree line: Frostfang's conifers stop at the snowfields because their band ends,
    /// not because someone hand-placed the edge of a forest.
    /// </summary>
    [Export] public Vector2 HeightRange { get; set; } = new(-9999f, 9999f);

    /// <summary>
    /// How strongly this species gathers into stands and leaves clearings, 0..1. 0 is the old
    /// behaviour — an even Poisson scatter across everything the other rules allow.
    ///
    /// ⚠️ <b>EVEN SPACING IS THE MOST RECOGNISABLE PATTERN THERE IS.</b> A minimum-spacing rejection
    /// sampler produces a field with no gaps and no thickets, which from any height reads instantly
    /// as generated — the eye finds the regularity long before it finds a repeated model. Gating
    /// acceptance on a low-frequency noise field at <see cref="ClumpScale"/> metres gives the same
    /// instance count a shape: copses, thin margins and open ground, none of it authored.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Clumping { get; set; }

    /// <summary>Metres per cycle of the clumping field — roughly the width of one stand.</summary>
    [Export(PropertyHint.Range, "5,200,1")] public float ClumpScale { get; set; } = 34f;

    /// <summary>
    /// Colour saturation of this layer's source model, 0 grey to 1 untouched. Anything but 1 swaps
    /// the model's own material for the shared scatter shader.
    ///
    /// ⚠️ <b><see cref="Tint"/> CANNOT DO THIS AND THAT IS WHY IT IS HERE.</b> An instance colour
    /// MULTIPLIES the albedo, and a multiply darkens a hue without ever draining it. Frostfang and
    /// the Ember Crown share one rock model whose atlas is olive; every blue tint anyone tried still
    /// produced a mossy green boulder sitting on a glacier. Draining it to about 0.35 and then
    /// tinting cold is what makes the same asset read as frost-shattered stone in one region and
    /// lichened granite in the other.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Saturation { get; set; } = 1f;

    [ExportGroup("HLOD proxy")]
    [Export(PropertyHint.Enum, "None,Cone,Box")] public int HlodShape { get; set; }
    [Export(PropertyHint.Range, "2,32,1")] public int HlodReduction { get; set; } = 4;
    [Export(PropertyHint.Range, "10,500,5")] public float HlodRangeBegin { get; set; } = 105f;
    [Export(PropertyHint.Range, "20,800,5")] public float HlodRangeEnd { get; set; } = 280f;
    [Export] public Color HlodColor { get; set; } = new(0.24f, 0.27f, 0.30f);
    [Export] public Vector3 HlodScale { get; set; } = Vector3.One;
}
