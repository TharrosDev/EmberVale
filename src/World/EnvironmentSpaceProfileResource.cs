using Godot;

namespace Embervale.World;

/// <summary>Scoped interior, dungeon or sequence contribution; never owns a WorldEnvironment.</summary>
[GlobalClass]
public partial class EnvironmentSpaceProfileResource : Resource
{
    [Export] public float AmbientScale { get; set; } = .5f;
    [Export] public float FogScale { get; set; } = .15f;
    [Export] public float ExposureScale { get; set; } = 1.12f;
    [Export] public float Shelter { get; set; } = 1f;
    [Export] public Color FogTint { get; set; } = Colors.White;
}
