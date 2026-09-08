using Godot;

namespace Embervale.World;

/// <summary>Explicit art override, spatially blended inside its oriented box. No collision or gameplay effects.</summary>
[GlobalClass]
public partial class EnvironmentVolume : Node3D
{
    [Export] public Vector3 Size { get; set; } = new(8, 4, 8);
    [Export] public float BlendDistance { get; set; } = 2f;
    [Export] public int Priority { get; set; }
    [Export] public EnvironmentSpaceProfileResource? Profile { get; set; }
    public override void _Ready() => AddToGroup("environment_volumes");
    public float WeightAt(Vector3 position)
    {
        Vector3 local = ToLocal(position).Abs();
        Vector3 remaining = Size.Abs() * .5f - local;
        return Mathf.Clamp(Mathf.Min(remaining.X, Mathf.Min(remaining.Y, remaining.Z)) / Mathf.Max(.01f, BlendDistance), 0, 1);
    }
}
