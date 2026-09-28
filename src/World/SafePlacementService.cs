using System.Collections.Generic;
using Embervale.Combat;
using Godot;

namespace Embervale.World;

/// <summary>
/// The authoritative actor-placement contract for new game, restores, travel, portals, encounters
/// and scripted teleports. It validates real physics, slope, capsule clearance and optional nav;
/// analytic terrain is only a candidate generator, never proof that collision is resident.
///
/// <b>Search order.</b> The desired point; then rings round it out to <c>searchRadius</c>
/// (<see cref="PlacementSearch"/>), nearest first; then each fallback anchor, and rings round that.
/// The first candidate that passes every check wins, so an accepted point is never further than
/// <c>searchRadius + maxCorrection</c> from where it was asked for.
///
/// <b>Diagnostics.</b> Pass a <see cref="SafePlacementReport"/> to learn what was tried and why each
/// candidate was refused; the loading gate logs its
/// <see cref="SafePlacementReport.Summary"/> when they give up, which is the difference between
/// "collision is not resident yet" and "this landing is inside a building".
///
/// ⚠️ <b>A RESOLVED POINT IS THE CAPSULE'S CENTRE CLEARANCE ABOVE THE GROUND, NOT THE GROUND.</b>
/// Feet-origin bodies (the player, enemies) settle the short distance onto it on their first physics
/// step; centre-origin bodies stand exactly there. Either way nothing is embedded on assignment.
/// </summary>
public static class SafePlacementService
{
    public const float DefaultRadius = 0.42f;
    public const float DefaultHeight = 1.8f;
    public const float DefaultMaxCorrection = 6f;
    public const float DefaultMaxSlopeDegrees = 44f;

    /// <summary>How far round a refused point the ring search looks by default. Three metres is
    /// enough to step out of a doorway, a crate stack or a boulder, and not enough to move a landing
    /// somewhere the author would not recognise.</summary>
    public const float DefaultSearchRadius = 3f;

    /// <summary>Gap between search rings, and between candidates round each ring.</summary>
    public const float SearchSpacing = 1f;
    public const float SearchArcSpacing = 1.2f;

    public static bool TryResolve(
        Node3D context, Vector3 desired, out Vector3 resolved,
        bool requireNavigation = false,
        IReadOnlyList<Vector3>? fallbackAnchors = null,
        float maxCorrection = DefaultMaxCorrection,
        float capsuleRadius = DefaultRadius,
        float capsuleHeight = DefaultHeight,
        float searchRadius = DefaultSearchRadius,
        SafePlacementReport? report = null)
    {
        resolved = desired;
        if (!context.IsInsideTree())
        {
            report?.Record(PlacementRejection.NotInTree);
            return false;
        }

        using var capsule = new CapsuleShape3D
        {
            Radius = capsuleRadius,
            Height = Mathf.Max(capsuleHeight, capsuleRadius * 2f),
        };
        var probe = new Probe(context, capsule, requireNavigation, maxCorrection, capsuleHeight);

        PlacementRejection first = probe.Test(desired, out resolved);
        report?.Record(first);
        if (first == PlacementRejection.None)
        {
            return true;
        }

        // A refusal that no neighbouring point can fix — there is no world, or no navigation yet —
        // is the verdict for every candidate. Running forty more queries to hear it forty more times
        // only costs the frame.
        if (first is PlacementRejection.NotInTree or PlacementRejection.NavigationUnavailable)
        {
            resolved = desired;
            return false;
        }

        if (SearchAround(probe, desired, searchRadius, report, out resolved))
        {
            return true;
        }

        if (fallbackAnchors != null)
        {
            foreach (Vector3 anchor in fallbackAnchors)
            {
                PlacementRejection rejection = probe.Test(anchor, out resolved);
                report?.Record(rejection);
                if (rejection == PlacementRejection.None ||
                    SearchAround(probe, anchor, searchRadius, report, out resolved))
                {
                    return true;
                }
            }
        }

        resolved = desired;
        return false;
    }

    private static bool SearchAround(
        Probe probe, Vector3 center, float searchRadius, SafePlacementReport? report, out Vector3 resolved)
    {
        foreach ((float x, float z) in PlacementSearch.RingOffsets(searchRadius, SearchSpacing, SearchArcSpacing))
        {
            PlacementRejection rejection = probe.Test(center + new Vector3(x, 0f, z), out resolved);
            report?.Record(rejection);
            if (rejection == PlacementRejection.None)
            {
                return true;
            }
        }
        resolved = center;
        return false;
    }

    /// <summary>One placement's shared query state: the world, the capsule, and the limits every
    /// candidate is judged against. Built once per <see cref="TryResolve"/>, not per candidate.</summary>
    private readonly struct Probe
    {
        private readonly World3D _world;
        private readonly Rid _exclude;
        private readonly bool _hasExclude;
        private readonly CapsuleShape3D _capsule;
        private readonly bool _requireNavigation;
        private readonly float _maxCorrection;
        private readonly float _centerClearance;

        public Probe(Node3D context, CapsuleShape3D capsule, bool requireNavigation, float maxCorrection,
            float capsuleHeight)
        {
            _world = context.GetWorld3D();
            _hasExclude = context is CollisionObject3D;
            _exclude = context is CollisionObject3D body ? body.GetRid() : default;
            _capsule = capsule;
            _requireNavigation = requireNavigation;
            _maxCorrection = maxCorrection;
            _centerClearance = (capsuleHeight * 0.5f) + 0.06f;
        }

        public PlacementRejection Test(Vector3 desired, out Vector3 resolved)
        {
            resolved = desired;
            Rid map = _world.NavigationMap;
            Vector3 candidate = desired;
            // A newly created world has a valid map RID before its first synchronization.
            // Optional navigation must fall back to real physics until queries are available;
            // required navigation still rejects the candidate below.
            if (map.IsValid && NavigationServer3D.MapGetIterationId(map) > 0)
            {
                Vector3 onNavigation = NavigationServer3D.MapGetClosestPoint(map, desired);
                if (onNavigation.DistanceSquaredTo(desired) <= _maxCorrection * _maxCorrection)
                {
                    candidate = onNavigation;
                }
                else if (_requireNavigation)
                {
                    return PlacementRejection.OffNavigation;
                }
            }
            else if (_requireNavigation)
            {
                return PlacementRejection.NavigationUnavailable;
            }

            Vector3 from = candidate + (Vector3.Up * _maxCorrection);
            Vector3 to = candidate + (Vector3.Down * _maxCorrection);
            var ray = PhysicsRayQueryParameters3D.Create(from, to, CombatLayers.WorldStatic);
            if (_hasExclude)
            {
                ray.Exclude = new Godot.Collections.Array<Rid> { _exclude };
            }
            Godot.Collections.Dictionary hit = _world.DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0 || !hit.TryGetValue("position", out Variant positionValue) ||
                !hit.TryGetValue("normal", out Variant normalValue))
            {
                return PlacementRejection.NoGround;
            }

            Vector3 ground = positionValue.AsVector3();
            Vector3 normal = normalValue.AsVector3().Normalized();
            if (ground.DistanceTo(desired) > _maxCorrection)
            {
                return PlacementRejection.TooFar;
            }
            if (Mathf.RadToDeg(normal.AngleTo(Vector3.Up)) > DefaultMaxSlopeDegrees)
            {
                return PlacementRejection.TooSteep;
            }

            var shape = new PhysicsShapeQueryParameters3D
            {
                Shape = _capsule,
                Transform = new Transform3D(Basis.Identity, ground + (Vector3.Up * _centerClearance)),
                CollisionMask = CombatLayers.WorldStatic | CombatLayers.WorldDynamic,
                CollideWithAreas = false,
                CollideWithBodies = true,
            };
            if (_hasExclude)
            {
                shape.Exclude = new Godot.Collections.Array<Rid> { _exclude };
            }
            if (_world.DirectSpaceState.IntersectShape(shape, 1).Count > 0)
            {
                return PlacementRejection.Blocked;
            }

            // The capsule's centre over the ground, not the hit point: the probe validated a capsule
            // standing HERE, and a centre-origin body assigned the bare hit point would be embedded
            // halfway through the terrain. A feet-origin body drops the clearance on its first step.
            resolved = ground + (Vector3.Up * _centerClearance);
            return PlacementRejection.None;
        }
    }
}
