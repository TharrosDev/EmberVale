using System.Collections.Generic;
using Embervale.Combat;
using Godot;

namespace Embervale.World;

/// <summary>
/// The authoritative actor-placement contract for new game, restores, travel, portals, encounters
/// and scripted teleports. It validates real physics, slope, capsule clearance and optional nav;
/// analytic terrain is only a candidate generator, never proof that collision is resident.
/// </summary>
public static class SafePlacementService
{
    public const float DefaultRadius = 0.42f;
    public const float DefaultHeight = 1.8f;
    public const float DefaultMaxCorrection = 6f;
    public const float DefaultMaxSlopeDegrees = 44f;

    public static bool TryResolve(
        Node3D context, Vector3 desired, out Vector3 resolved,
        bool requireNavigation = false,
        IReadOnlyList<Vector3>? fallbackAnchors = null,
        float maxCorrection = DefaultMaxCorrection,
        float capsuleRadius = DefaultRadius,
        float capsuleHeight = DefaultHeight)
    {
        if (TryCandidate(context, desired, out resolved, requireNavigation, maxCorrection,
                capsuleRadius, capsuleHeight))
        {
            return true;
        }

        if (fallbackAnchors != null)
        {
            foreach (Vector3 anchor in fallbackAnchors)
            {
                if (TryCandidate(context, anchor, out resolved, requireNavigation, maxCorrection,
                        capsuleRadius, capsuleHeight))
                {
                    return true;
                }
            }
        }

        resolved = desired;
        return false;
    }

    private static bool TryCandidate(
        Node3D context, Vector3 desired, out Vector3 resolved, bool requireNavigation,
        float maxCorrection, float capsuleRadius, float capsuleHeight)
    {
        resolved = desired;
        if (!context.IsInsideTree())
        {
            return false;
        }

        World3D world = context.GetWorld3D();
        Rid map = world.NavigationMap;
        Vector3 candidate = desired;
        if (map.IsValid)
        {
            Vector3 onNavigation = NavigationServer3D.MapGetClosestPoint(map, desired);
            if (onNavigation.DistanceSquaredTo(desired) <= maxCorrection * maxCorrection)
            {
                candidate = onNavigation;
            }
            else if (requireNavigation)
            {
                return false;
            }
        }
        else if (requireNavigation)
        {
            return false;
        }

        Vector3 from = candidate + (Vector3.Up * maxCorrection);
        Vector3 to = candidate + (Vector3.Down * maxCorrection);
        var ray = PhysicsRayQueryParameters3D.Create(from, to, CombatLayers.WorldStatic);
        if (context is CollisionObject3D body)
        {
            ray.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        }
        Godot.Collections.Dictionary hit = world.DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || !hit.TryGetValue("position", out Variant positionValue) ||
            !hit.TryGetValue("normal", out Variant normalValue))
        {
            return false;
        }

        Vector3 ground = positionValue.AsVector3();
        Vector3 normal = normalValue.AsVector3().Normalized();
        if (ground.DistanceTo(desired) > maxCorrection ||
            Mathf.RadToDeg(normal.AngleTo(Vector3.Up)) > DefaultMaxSlopeDegrees)
        {
            return false;
        }

        var capsule = new CapsuleShape3D
        {
            Radius = capsuleRadius,
            Height = Mathf.Max(capsuleHeight, capsuleRadius * 2f),
        };
        float centerClearance = (capsuleHeight * 0.5f) + 0.06f;
        var shape = new PhysicsShapeQueryParameters3D
        {
            Shape = capsule,
            Transform = new Transform3D(Basis.Identity,
                ground + (Vector3.Up * centerClearance)),
            CollisionMask = CombatLayers.WorldStatic | CombatLayers.WorldDynamic,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        if (context is CollisionObject3D collider)
        {
            shape.Exclude = new Godot.Collections.Array<Rid> { collider.GetRid() };
        }
        bool blocked = world.DirectSpaceState.IntersectShape(shape, 1).Count > 0;
        capsule.Dispose();
        if (blocked)
        {
            return false;
        }

        // CharacterBody origins are at the centre of their capsule, not at its feet. Returning the
        // hit point itself would validate an empty capsule and then embed the actual actor halfway
        // through the terrain on assignment.
        resolved = ground + (Vector3.Up * centerClearance);
        return true;
    }
}
