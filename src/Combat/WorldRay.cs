using System;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A ray against solid world geometry that ignores people.
///
/// <para>⚠️ <b>Actors sit on the same physics layer as walls.</b> <c>CharacterEntity</c> defaults to
/// <c>collision_layer 1</c>, which is <see cref="CombatLayers.WorldStatic"/>, so a plain world ray
/// reports an enemy's capsule as a wall. An arrow that stopped there would die on the body it was
/// meant to hurt, and an aim-assist line of sight would be "blocked" by the very target. Any collider
/// that belongs to an entity is therefore stepped past, by re-casting with it excluded.</para>
///
/// <para>Owns reusable query objects (one per arrow, one per aim controller) so a hot path builds none
/// per frame. Dispose it with its owner.</para>
/// </summary>
public sealed class WorldRay : IDisposable
{
    /// <summary>How many actors one ray steps through before conservatively treating the last
    /// collider as an obstruction. Exhausting a query budget never proves a segment clear.</summary>
    private const int MaxActorSkips = 64;

    private readonly PhysicsRayQueryParameters3D _query = new()
    {
        CollisionMask = CombatLayers.PhysicalWorld,
        CollideWithAreas = false,
        CollideWithBodies = true,
    };

    private readonly Godot.Collections.Array<Rid> _base = new();
    private readonly Godot.Collections.Array<Rid> _extended = new();

    /// <summary>Sets the body the ray never hits (the shooter).</summary>
    public void Ignore(Rid body)
    {
        _base.Clear();
        if (body.IsValid)
        {
            _base.Add(body);
        }
        _query.Exclude = _base;
    }

    /// <summary>The first solid hit on the segment, or null when it is clear. The normal points away
    /// from the surface.</summary>
    public (Vector3 Point, Vector3 Normal)? FirstSolid(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
    {
        _query.From = from;
        _query.To = to;
        _query.Exclude = _base;

        _extended.Clear();
        for (int skips = 0; ; skips++)
        {
            Godot.Collections.Dictionary hit = space.IntersectRay(_query);
            if (hit.Count == 0)
            {
                return null;
            }

            Node? collider = hit["collider"].AsGodotObject() as Node;
            if (collider == null || EntityNode.FindOwner(collider) == null || skips >= MaxActorSkips)
            {
                return (hit["position"].AsVector3(), hit["normal"].AsVector3());
            }

            // A body belonging to an actor: not a wall. Cast again with it out of the way.
            if (_extended.Count == 0)
            {
                foreach (Rid rid in _base)
                {
                    _extended.Add(rid);
                }
            }
            _extended.Add(hit["rid"].AsRid());
            _query.Exclude = _extended;
        }
    }

    public void Dispose()
    {
        _query.Dispose();
        _base.Clear();
        _extended.Clear();
    }
}
