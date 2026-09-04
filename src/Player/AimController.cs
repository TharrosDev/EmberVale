using Embervale.Entities;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Points the spellcasting aim node at whatever the crosshair converges on, so a bolt goes where the
/// reticle is instead of along the head's forward.
///
/// <para>In first person the convergence point lies on the pivot's own forward axis, so the node's
/// aim is unchanged from before the camera rig existed — that is the invariant that keeps this safe
/// for the shipping mode.</para>
/// </summary>
[GlobalClass]
public partial class AimController : EntityComponent
{
    /// <summary>How far the crosshair convergence ray reaches before falling back to a far point.</summary>
    private const float AimTraceDistance = 200f;

    private PlayerCameraRig? _rig;
    private PlayerPhysicsQueries? _queries;

    /// <summary>The node spellcasting aims along, injected by <see cref="PlayerFactory"/>. It sits
    /// on the body but is re-aimed each frame at the point the crosshair converges on.</summary>
    public Node3D? AimNode { get; set; }

    protected override void OnInitialize()
    {
        _rig = Entity!.GetComponent<PlayerCameraRig>();
        _queries = Entity.GetComponent<PlayerPhysicsQueries>();
    }

    public void Tick()
    {
        if (AimNode == null || _queries == null || _rig?.Camera is not { } camera)
        {
            return;
        }

        Vector3 from = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalTransform.Basis.Z;
        Vector3 focus = _queries.Raycast(from, forward, AimTraceDistance) is { } hit
            ? hit.Point
            : from + (forward * AimTraceDistance);

        Vector3 direction = CameraRigMath.AimDirection(AimNode.GlobalPosition, focus);
        if (Mathf.Abs(direction.Dot(Vector3.Up)) > 0.999f)
        {
            return; // straight up/down: LookAt has no valid basis, so keep the last aim
        }

        AimNode.LookAt(AimNode.GlobalPosition + direction, Vector3.Up);
    }
}
