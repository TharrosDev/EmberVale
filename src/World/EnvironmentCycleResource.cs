using Godot;

namespace Embervale.World;

/// <summary>Shared authored day cycle; regions and weather compose over these keys.</summary>
[GlobalClass]
public partial class EnvironmentCycleResource : Resource
{
    [Export] public Godot.Collections.Array<EnvironmentKeyframeResource> Keys { get; set; } = new();
    [Export] public float TransitionSeconds { get; set; } = 4f;

    /// <summary>
    /// Exponential fog density in clear weather, before the region's <c>HazeScale</c> and humidity.
    /// The fraction of a thing hidden at distance d is <c>1 - exp(-density * d)</c>, so this one
    /// number decides both how soft the near field is and whether a landmark on the skyline can be
    /// seen at all: 0.0012 hides half of something 600 m away, 0.003 hides five sixths of it.
    /// </summary>
    [Export] public float ClearFogDensity { get; set; } = 0.0018f;
    [Export] public Color MoonColor { get; set; } = new(0.65f, 0.73f, 0.86f);
    [Export] public float Contrast { get; set; } = 1.02f;
    [Export] public float Saturation { get; set; } = 0.96f;
    [Export] public float GlowIntensity { get; set; } = 0.22f;

    /// <summary>
    /// How much of the fog's colour is taken from the sky behind it (0 to 1), outdoors. Distant
    /// ground and landmarks then fade toward the sky they stand against rather than toward one flat
    /// fog colour, which is what reads as distance. 0 (the default) is the fog as it was. The
    /// Performance tier leaves it off: it costs a sky lookup per pixel.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AerialPerspective { get; set; }

    /// <summary>
    /// Density of the fog that gathers in low ground, per metre of depth below
    /// <see cref="HeightFogDrop"/> metres under the camera. It never touches ground at the
    /// viewer's own level, only what lies well below: a valley seen from a ridge. 0 (the default)
    /// is off.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.05,0.001")] public float HeightFogDensity { get; set; }

    /// <summary>Metres below the camera at which the low-ground fog begins.</summary>
    [Export(PropertyHint.Range, "5,120,1")] public float HeightFogDrop { get; set; } = 25f;
}
