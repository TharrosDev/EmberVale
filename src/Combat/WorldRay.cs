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
    /// <summary>How many actors one ray will step through before giving up and calling it clear.</summary>
    private const int MaxActorSkips = 4;

    private readonly PhysicsRayQueryParameters3D _query = new()
    {
        CollisionMask = CombatLayers.PhysicalWorld,
        CollideWithAreas = false,
        CollideWithBodies = true,
    };

    private Godot.Collections.Array<Rid> _base = new();

    /// <summary>Sets the body the ray never hits (the shooter).</summary>
    public void Ignore(Rid body)
    {
        _base = body.IsValid ? new Godot.Collections.Array<Rid> { body } : new Godot.Collections.Array<Rid>();
        _query.Exclude = _base;
    }

    /// <summary>The first solid hit on the segment, or null when it is clear. The normal points away
    /// from the surface.</summary>
    public (Vector3 Point, Vector3 Normal)? FirstSolid(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
    {
        _query.From = from;
        _query.To = to;
        _query.Exclude = _base;

        Godot.Collections.Array<Rid>? extended = null;
        for (int skips = 0; skips <= MaxActorSkips; skips++)
        {
            Godot.Collections.Dictionary hit = space.IntersectRay(_query);
            if (hit.Count == 0)
            {
                return null;
            }

            Node? collider = hit["collider"].AsGodotObject() as Node;
            if (collider == null || EntityNode.FindOwner(collider) == null)
            {
                return (hit["position"].AsVector3(), hit["normal"].AsVector3());
            }

            // A body belonging to an actor: not a wall. Cast again with it out of the way.
            extended ??= new Godot.Collections.Array<Rid>(_base);
            extended.Add(hit["rid"].AsRid());
            _query.Exclude = extended;
        }

        return null;
    }

    public void Dispose()
    {
        _query.Dispose();
    }
}
