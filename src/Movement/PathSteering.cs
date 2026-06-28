namespace Embervale.Movement;

/// <summary>
/// The pure rule behind navmesh-aware <see cref="LocomotionComponent"/> steering (Phase 27A): an
/// agent follows a path one corner at a time, but it has only <em>arrived</em> when it reaches the
/// <em>final</em> target — never an intermediate corner (judging arrival by the corner would stop the
/// actor short at every bend). Godot-free so it is unit-testable.
/// </summary>
public static class PathSteering
{
    /// <summary>
    /// Whether the actor should keep steering this frame. <paramref name="distanceToCorner"/> is the
    /// planar distance to the next path waypoint, <paramref name="distanceToFinalTarget"/> the planar
    /// distance to the ultimate destination, <paramref name="stopDistance"/> the arrival tolerance.
    /// Steer only while still short of the final target and the corner is far enough to give a
    /// meaningful direction.
    /// </summary>
    public static bool ShouldSteer(float distanceToCorner, float distanceToFinalTarget, float stopDistance) =>
        distanceToFinalTarget > stopDistance && distanceToCorner > 0.01f;
}
