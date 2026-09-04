using System.Collections.Generic;
using Embervale.Entities;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The player's shared physics queries, and the objects they reuse.
///
/// <para>⚠️ <b>THESE ARE REUSED, NOT REBUILT PER FRAME.</b> The interaction sensor and the aim
/// controller each fire a ray every physics frame and the third-person rig sweeps a sphere, so
/// building them per call cost five native <c>RefCounted</c> objects a frame — 300 a second of pure
/// churn on the hottest path in the game.</para>
///
/// <para>They live in their own component rather than in each caller precisely because there are
/// now several callers: three components sharing one exclusion list and three cached query objects
/// is the point, and duplicating the pooling per component would undo the optimisation it exists
/// for. The component outlives every frame that uses it and nothing else holds a reference, which
/// is what makes disposing in <see cref="OnTeardown"/> safe.</para>
/// </summary>
[GlobalClass]
public partial class PlayerPhysicsQueries : EntityComponent
{
    private PhysicsRayQueryParameters3D? _rayQuery;
    private PhysicsShapeQueryParameters3D? _sweepQuery;
    private PhysicsShapeQueryParameters3D? _overlapQuery;
    private Godot.Collections.Array<Rid>? _selfExclusion;

    /// <summary>The owning body, or null if this component is not on a character.</summary>
    private CharacterBody3D? Body => Entity?.Body as CharacterBody3D;

    /// <summary>One ray against everything the player can look at, excluding their own body.</summary>
    public (Node? Collider, Vector3 Point)? Raycast(Vector3 from, Vector3 direction, float distance)
    {
        if (Body is not { } body)
        {
            return null;
        }

        PhysicsRayQueryParameters3D query = _rayQuery ??= new PhysicsRayQueryParameters3D
        {
            Exclude = SelfExclusion(body),
        };
        query.From = from;
        query.To = from + (direction * distance);

        Godot.Collections.Dictionary hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0
            ? null
            : (hit["collider"].AsGodotObject() as Node, hit["position"].AsVector3());
    }

    /// <summary>
    /// Sweeps a sphere along <paramref name="motion"/> and returns the fraction of it that is
    /// travelled without overlapping anything — 1 when the way is clear. The camera spring's
    /// question, asked in the form it wants the answer in.
    /// </summary>
    public float SafeSweepFraction(Vector3 origin, Vector3 motion, float radius, uint collisionMask)
    {
        if (Body is not { } body)
        {
            return 1f;
        }

        PhysicsShapeQueryParameters3D query = _sweepQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D(),
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = SelfExclusion(body),
        };
        ((SphereShape3D)query.Shape).Radius = radius;
        query.CollisionMask = collisionMask;
        query.Transform = new Transform3D(Basis.Identity, origin);
        query.Motion = motion;

        // CastMotion returns [safe, unsafe] fractions of the motion; the safe one is the last
        // position the sphere occupies without overlapping anything.
        float[] fractions = body.GetWorld3D().DirectSpaceState.CastMotion(query);
        return fractions.Length > 0 ? Mathf.Clamp(fractions[0], 0f, 1f) : 1f;
    }

    /// <summary>
    /// The entities overlapping a sphere at <paramref name="centre"/>. Materialised into a list
    /// rather than yielded, because the caller frees what it finds — the auto-pickup sweep collects
    /// items, and an item empties and frees itself as it is taken.
    /// </summary>
    public List<IEntity> OverlapSphere(Vector3 centre, float radius, int maxResults)
    {
        var found = new List<IEntity>();
        if (Body is not { } body)
        {
            return found;
        }

        PhysicsShapeQueryParameters3D query = _overlapQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D(),
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = SelfExclusion(body),
        };
        ((SphereShape3D)query.Shape).Radius = radius;
        query.Transform = new Transform3D(Basis.Identity, centre);

        foreach (Godot.Collections.Dictionary hit in
            body.GetWorld3D().DirectSpaceState.IntersectShape(query, maxResults))
        {
            if (hit["collider"].AsGodotObject() is Node collider && EntityNode.FindOwner(collider) is { } owner)
            {
                found.Add(owner);
            }
        }

        return found;
    }

    protected override void OnTeardown()
    {
        _rayQuery?.Dispose();
        _sweepQuery?.Dispose();
        _overlapQuery?.Dispose();
        _rayQuery = null;
        _sweepQuery = null;
        _overlapQuery = null;
        _selfExclusion = null;
    }

    /// <summary>The player's own body, as the one-element exclusion list every query here shares.
    /// The RID is stable for the life of the body, so this is built once.</summary>
    private Godot.Collections.Array<Rid> SelfExclusion(CharacterBody3D body) =>
        _selfExclusion ??= new Godot.Collections.Array<Rid> { body.GetRid() };
}
