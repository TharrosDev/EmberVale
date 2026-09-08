using Godot;

namespace Embervale.World;

/// <summary>Shared authored day cycle; regions and weather compose over these keys.</summary>
[GlobalClass]
public partial class EnvironmentCycleResource : Resource
{
    [Export] public Godot.Collections.Array<EnvironmentKeyframeResource> Keys { get; set; } = new();
    [Export] public float TransitionSeconds { get; set; } = 4f;
    [Export] public float ClearFogDensity { get; set; } = 0.0018f;
    [Export] public Color MoonColor { get; set; } = new(0.65f, 0.73f, 0.86f);
    [Export] public float Contrast { get; set; } = 1.02f;
    [Export] public float Saturation { get; set; } = 0.96f;
    [Export] public float GlowIntensity { get; set; } = 0.22f;
}
