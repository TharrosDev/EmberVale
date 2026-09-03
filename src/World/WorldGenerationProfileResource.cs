using Godot;

namespace Embervale.World;

/// <summary>Art-directable, versioned recipe shared by every region. The resource is only an
/// editor/data boundary; generation itself consumes the engine-free immutable settings object.</summary>
[GlobalClass]
public partial class WorldGenerationProfileResource : Resource
{
    [ExportGroup("Identity")]
    [Export] public int MasterSeed { get; set; } = 3800;
    [Export(PropertyHint.Range, "2,128,1")] public int GeneratorVersion { get; set; } = 2;

    [ExportGroup("Macro geography")]
    [Export(PropertyHint.Range, "-100,200,0.5")] public float BaseElevation { get; set; }
    [Export(PropertyHint.Range, "80,2000,10")] public float MacroScale { get; set; } = 420f;
    [Export(PropertyHint.Range, "0,80,0.5")] public float MacroRelief { get; set; } = 7f;
    [Export(PropertyHint.Range, "60,1200,10")] public float MountainScale { get; set; } = 230f;
    [Export(PropertyHint.Range, "0.1,0.9,0.01")] public float MountainPrevalence { get; set; } = 0.48f;
    [Export(PropertyHint.Range, "0,160,1")] public float MountainHeight { get; set; } = 14f;
    [Export(PropertyHint.Range, "0,40,0.5")] public float ValleyStrength { get; set; } = 5f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ErosionStrength { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "0,12,0.1")] public float LocalRelief { get; set; } = 1.4f;
    [Export(PropertyHint.Range, "0.25,8,0.25")] public float DetailScale { get; set; } = 2.75f;

    /// <summary>Metres over which an authored road or yard calms the macro relief around itself.
    /// Wide on purpose: a narrow value is a trench with a road in it. See
    /// <see cref="WorldGenerationSettings.RouteCalm"/> for why this exists at all.</summary>
    [Export(PropertyHint.Range, "0,160,5")] public float RouteCalm { get; set; } = 45f;

    [ExportGroup("Climate")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float Temperature { get; set; } = 0.58f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Moisture { get; set; } = 0.52f;
    [Export(PropertyHint.Range, "-20,180,1")] public float SnowLine { get; set; } = 34f;

    [ExportGroup("Hydrology")]
    [Export(PropertyHint.Range, "6,48,1")] public float HydrologyCellSize { get; set; } = 12f;
    [Export(PropertyHint.Range, "3,200,1")] public float RiverThreshold { get; set; } = 16f;
    [Export(PropertyHint.Range, "0.5,16,0.25")] public float RiverWidth { get; set; } = 3.2f;
    [Export(PropertyHint.Range, "0.2,8,0.1")] public float RiverDepth { get; set; } = 1.8f;

    public WorldGenerationSettings Settings() => new()
    {
        Seed = MasterSeed,
        Version = GeneratorVersion,
        BaseElevation = BaseElevation,
        MacroScale = MacroScale,
        MacroRelief = MacroRelief,
        MountainScale = MountainScale,
        MountainPrevalence = MountainPrevalence,
        MountainHeight = MountainHeight,
        ValleyStrength = ValleyStrength,
        ErosionStrength = ErosionStrength,
        LocalRelief = LocalRelief,
        DetailScale = DetailScale,
        RouteCalm = RouteCalm,
        Temperature = Temperature,
        Moisture = Moisture,
        SnowLine = SnowLine,
        HydrologyCellSize = HydrologyCellSize,
        RiverThreshold = RiverThreshold,
        RiverWidth = RiverWidth,
        RiverDepth = RiverDepth,
    };
}
