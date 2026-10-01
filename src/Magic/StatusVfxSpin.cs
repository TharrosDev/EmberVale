using Godot;

namespace Embervale.Magic;

/// <summary>Turns its node about Y each frame; the status marks that orbit use it. A speed of 0 (Reduced
/// Motion) leaves them still.</summary>
public partial class StatusVfxSpin : Node3D
{
    public float DegreesPerSecond { get; set; }

    public override void _Process(double delta)
    {
        if (DegreesPerSecond != 0f)
        {
            RotateY(Mathf.DegToRad(DegreesPerSecond) * (float)delta);
        }
    }
}
