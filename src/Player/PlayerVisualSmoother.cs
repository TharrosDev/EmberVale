using Embervale.Entities;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Smooths what the player sees between physics ticks. The body moves at the physics rate and the
/// screen draws faster, so the camera and the body mesh are to be offset each drawn frame by the
/// interpolation residual between the last two physics positions, and the offset zeroed at the start
/// of every physics tick so aim, sweeps and hitboxes read exact positions. Presentation only: it
/// never moves the body, and there is no project-wide physics interpolation.
///
/// <para>It is added before the animation component and foot IK so that, once it does something,
/// it has run before they read the rig.</para>
///
/// <para>Seam: inert. It holds the two nodes it will offset and overrides no process callback, so
/// adding it changes nothing.</para>
/// </summary>
[GlobalClass]
public partial class PlayerVisualSmoother : EntityComponent
{
    /// <summary>The pitch pivot the camera rides. Injected by the factory.</summary>
    public Node3D? CameraPivot { get; set; }

    /// <summary>The visible body. Injected by the factory.</summary>
    public Node3D? BodyMesh { get; set; }
}
