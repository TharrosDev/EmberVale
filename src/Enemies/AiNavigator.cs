using System;
using Embervale.Entities;
using Embervale.Movement;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// How an AI actor gets from where it is to where it wants to be: navmesh steering, arrival,
/// standing still, and turning to face something.
///
/// <para><b>It is shared by the enemy brain and the companion brain, and that is the point.</b>
/// Both had their own copy of the three-answer navigation rule below, and the copies had already
/// drifted: the companion's ran <c>MapGetClosestPoint</c> — a navigation-server query — every single
/// frame where the enemy's paced it, and it had no turn-rate slew at all. Two implementations of one
/// rule is two places for the next fix to be applied to only one of.</para>
///
/// <para>It is a plain class owned by the component that steers, not a node: an enemy is already a
/// dozen nodes and this holds four fields and no lifetime of its own.</para>
/// </summary>
public sealed class AiNavigator
{
    /// <summary>How far off the navmesh an actor may be and still be considered standing on it.
    /// Generous, because an agent's own radius keeps its closest point at arm's length from a wall.</summary>
    private const float NavAnchorTolerance = 3f;

    /// <summary>Seconds between "am I on the navmesh at all" checks. The answer only changes when
    /// the actor walks off the mesh or a bake lands, and the query is a server round trip that would
    /// otherwise run for every moving actor every frame.</summary>
    private const double NavAnchorInterval = 0.25d;

    private readonly IEntity _owner;
    private readonly Node3D _body;
    private readonly NavigationAgent3D? _agent;

    private double _navAnchorTimer;
    private bool _navAnchored = true;

    public AiNavigator(IEntity owner, Node3D body, NavigationAgent3D? agent)
    {
        _owner = owner;
        _body = body;
        _agent = agent;
    }

    /// <summary>
    /// Walks toward <paramref name="target"/>, steering at the next navmesh corner but judging
    /// arrival against the FINAL target, so the actor does not stop short at a bend.
    /// </summary>
    /// <param name="airborne">True when the actor is off the ground: the navmesh is then the wrong
    /// map, because its corners route around obstacles the actor is flying over.</param>
    /// <param name="faceWish">Called with the steering direction when there is one, for an actor
    /// that turns to face where it is walking. Null for one that faces something else (a fighting
    /// enemy faces its target, not its feet).</param>
    public void MoveTowards(
        Vector3 target,
        double delta,
        bool sprint,
        float stopDistance,
        bool airborne = false,
        Action<Vector3>? faceWish = null)
    {
        if (NextPathPoint(target, delta, airborne) is not { } corner)
        {
            // Navigation is not usable here yet. Hold rather than walk: the alternative was steering
            // straight at the goal, which is a line through whatever is between.
            Stand(delta);
            return;
        }

        Vector3 toCorner = corner - _body.GlobalPosition;
        toCorner.Y = 0f;
        float cornerDistance = toCorner.Length();
        float finalDistance = HorizontalDistance(_body.GlobalPosition, target);
        Vector3 wish = PathSteering.ShouldSteer(cornerDistance, finalDistance, stopDistance)
            ? toCorner.Normalized()
            : Vector3.Zero;

        if (wish != Vector3.Zero)
        {
            faceWish?.Invoke(wish);
        }

        Locomotion?.Move(delta, wish, sprint, jump: false);
    }

    /// <summary>
    /// The next waypoint to steer toward, or <c>null</c> when there is no safe one and the actor
    /// should hold still.
    ///
    /// ⚠️ <b>THERE IS NO STRAIGHT-LINE FALLBACK, AND ITS ABSENCE IS THE POINT.</b> This used to
    /// return the target itself whenever the path query came back empty — which is the case both
    /// while a cell's navmesh is still baking (<see cref="World.CellNavBaker"/> defers a frame and
    /// bakes on a worker) and whenever a goal sits off the mesh. The result was every actor in a
    /// freshly streamed cell walking the shortest line to the player: through market stalls, through
    /// the smithy, through the arena wall. It read as a physics bug and it was a navigation one.
    ///
    /// Three answers, each honest about what it knows:
    /// <list type="bullet">
    /// <item>No agent at all, or airborne — no navigation was ever intended for this actor, or the
    /// mesh is the wrong map for it. Steer at the target.</item>
    /// <item>An agent whose map cannot place the actor — the bake has not landed, or the actor is
    /// off-mesh. Hold. The next tick asks again and it costs nothing.</item>
    /// <item>An unreachable goal on a usable mesh — steer to the closest point the mesh does have,
    /// which is as far as the actor can honestly get, instead of through the wall between.</item>
    /// </list>
    /// Re-targets the agent only when the goal actually moves, to avoid needless repaths.
    /// </summary>
    public Vector3? NextPathPoint(Vector3 target, double delta, bool airborne = false)
    {
        if (_agent == null || airborne)
        {
            return target;
        }

        Rid map = _agent.GetNavigationMap();
        if (!map.IsValid)
        {
            return null;
        }

        // Is this actor standing on navigable ground at all? An empty map (nothing baked yet)
        // answers Vector3.Zero, and an off-mesh actor answers somewhere far away; both mean the path
        // this frame would be a guess. Paced rather than asked every frame — see NavAnchorInterval.
        Vector3 here = _body.GlobalPosition;
        _navAnchorTimer -= delta;
        if (_navAnchorTimer <= 0d)
        {
            _navAnchorTimer = NavAnchorInterval;
            _navAnchored = NavigationServer3D.MapGetClosestPoint(map, here)
                .DistanceSquaredTo(here) <= NavAnchorTolerance * NavAnchorTolerance;
        }

        if (!_navAnchored)
        {
            return null;
        }

        if (_agent.TargetPosition.DistanceSquaredTo(target) > 0.01f)
        {
            _agent.TargetPosition = target;
        }

        if (_agent.IsTargetReachable())
        {
            return _agent.GetNextPathPosition();
        }

        // Unreachable: aim for the nearest place on the mesh that exists. Re-target rather than
        // returning it directly, so the actor still walks a path to it rather than a line.
        Vector3 nearest = NavigationServer3D.MapGetClosestPoint(map, target);
        if (nearest.DistanceSquaredTo(here) <= 0.04f)
        {
            return null; // already as close as the mesh goes
        }

        if (_agent.TargetPosition.DistanceSquaredTo(nearest) > 0.01f)
        {
            _agent.TargetPosition = nearest;
        }

        return _agent.IsTargetReachable() ? _agent.GetNextPathPosition() : null;
    }

    public void Stand(double delta) => Locomotion?.Move(delta, Vector3.Zero, sprint: false, jump: false);

    /// <summary>
    /// The nearest point on the navmesh to <paramref name="candidate"/>, or the ground under it when
    /// this actor has no agent or nothing is baked. Patrol points are snapped through here rather
    /// than taken raw, because a raw disc sample sits inside the smithy about as often as it sits on
    /// the road.
    /// </summary>
    public Vector3 SnapToWalkable(Vector3 candidate)
    {
        Rid map = _agent?.GetNavigationMap() ?? default;
        return map.IsValid
            ? NavigationServer3D.MapGetClosestPoint(map, candidate)
            : World.WorldGround.OnGround(candidate);
    }

    /// <summary>Turns to face a point — instantly, or slewed at <paramref name="turnSpeedDegrees"/>
    /// for a body too heavy to pivot on the spot. A snap-turn target is always dead ahead, which is
    /// why a slew is what lets a rear or flank attack set ever fire.</summary>
    public void FaceTowards(Vector3 target, float turnSpeedDegrees = 0f, double delta = 0d)
    {
        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) <= 0.0004f)
        {
            return;
        }

        if (turnSpeedDegrees <= 0f)
        {
            _body.LookAt(flat, Vector3.Up);
            return;
        }

        float desired = Mathf.Atan2(pos.X - flat.X, pos.Z - flat.Z);
        float step = Mathf.DegToRad(turnSpeedDegrees) * (float)delta;
        _body.Rotation = _body.Rotation with { Y = Mathf.RotateToward(_body.Rotation.Y, desired, step) };
    }

    /// <summary>Signed angle from this actor's facing to a point, in degrees — 0 dead ahead, ±180
    /// directly behind. Drives the directional attack sets.</summary>
    public float BearingTo(Vector3 target)
    {
        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) <= 0.0004f)
        {
            return 0f;
        }

        float desired = Mathf.Atan2(pos.X - flat.X, pos.Z - flat.Z);
        return Mathf.RadToDeg(Mathf.AngleDifference(_body.Rotation.Y, desired));
    }

    /// <summary>Plan distance. ⚠️ Every AI distance except the alert radius is horizontal, because a
    /// creature at the bottom of a cliff is not near the player at the top of one.</summary>
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.Y = 0f;
        b.Y = 0f;
        return a.DistanceTo(b);
    }

    /// <summary>Resolved per call, as both brains did: a locomotion component can be added or
    /// replaced during a body's life and caching it was never the contract.</summary>
    private LocomotionComponent? Locomotion => _owner.GetComponent<LocomotionComponent>();
}
