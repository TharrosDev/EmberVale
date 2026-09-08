using Godot;

namespace Embervale.World;

/// <summary>One lighting key in a cyclic day. Energies are scene-linear multipliers.</summary>
[GlobalClass]
public partial class EnvironmentKeyframeResource : Resource
{
    [Export] public float Hour { get; set; }
    [Export] public Color SunColor { get; set; } = Colors.White;
    [Export] public Color SkyColor { get; set; } = new(0.25f, 0.38f, 0.54f);
    [Export] public Color HorizonColor { get; set; } = new(0.65f, 0.68f, 0.70f);
    [Export] public float SunEnergy { get; set; } = 1f;
    [Export] public float MoonEnergy { get; set; }
    [Export] public float AmbientEnergy { get; set; } = 0.6f;
    [Export] public float SkyEnergy { get; set; } = 1f;
    [Export] public float FogEnergy { get; set; } = 0.8f;
    [Export] public float Exposure { get; set; } = 1f;
}
