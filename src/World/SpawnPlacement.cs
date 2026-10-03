using Godot;

namespace Embervale.World;

/// <summary>
/// Where a runtime-spawned actor is actually allowed to stand.
///
/// ⚠️ <b>IT EXISTS BECAUSE THE GROUP ORIGIN WAS THE ONLY THING ANYONE VALIDATED.</b> Both spawners —
/// <see cref="EncounterDirector"/> and <see cref="WorldEventDirector"/> — pick one ring point outside
/// the safe zone, check that one point, and then scatter every member of the band up to a metre
/// around it with an unvalidated jitter and the origin's own Y. On the flat greybox that was
/// harmless. On real terrain, and next to real buildings, it puts individual members inside a slope
/// or inside the smithy: an enemy embedded in geometry cannot path, cannot be reached, and cannot be
/// killed, so an ambient encounter or a world event never completes and the director's counter never
/// comes back down.
///
/// Two corrections, in order, and both are needed. The navmesh answers "is this somewhere an actor
/// can stand", which is what keeps a member out of a wall — but the mesh is a simplified surface and
/// its Y drifts from the terrain by a few centimetres, so the ground answers the height afterwards.
/// With no synchronized navigation map the placement service can still validate real ground and
/// capsule clearance. A rejected point is never replaced by an unchecked analytic height: callers
/// skip materializing that actor and keep their live count honest.
/// </summary>
public static class SpawnPlacement
{
    /// <summary>How far a spawn point may be nudged onto the navmesh. Beyond this the nearest
    /// walkable ground is somewhere else entirely and moving the actor there would scatter a band
    /// across the map; a point that fails real ground/clearance checks is refused.</summary>
    private const float MaxSnapDistance = SafePlacementService.DefaultMaxCorrection;

    /// <summary>Finds a real capsule-clear landing, or refuses materialization.</summary>
    public static bool TryResolve(Node3D context, Vector3 desired, out Vector3 resolved) =>
        SafePlacementService.TryResolve(
            context, desired, out resolved, maxCorrection: MaxSnapDistance);
}
