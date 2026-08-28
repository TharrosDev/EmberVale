using Godot;

namespace Embervale.World;

/// <summary>
/// One cell-local authored route segment. Segments let a location's roads and trails follow its
/// actual entrances, services, combat loops, and landmarks instead of forcing every cell through a
/// single cardinal strip.
/// </summary>
[GlobalClass]
public partial class WorldPathSegmentResource : Resource
{
    [Export] public Vector2 Start { get; set; } = new(0f, 26f);
    [Export] public Vector2 End { get; set; } = new(0f, -26f);
    [Export(PropertyHint.Range, "1,16,0.25")] public float Width { get; set; } = 4f;
    [Export(PropertyHint.Range, "0,8,0.25")] public float Shoulder { get; set; } = 1.5f;
}
