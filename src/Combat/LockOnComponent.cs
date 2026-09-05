using System.Collections.Generic;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Lock-on / soft target (Phase 29H), built out from the Phase 18 <c>FocusedEntity</c>. Holds the current
/// locked <see cref="Target"/>, acquires the nearest hostile (or a preferred aimed-at entity) on toggle,
/// cycles between nearby hostiles, and drops a target that dies or leaves range. The owning controller
/// faces the body at the target; the HUD reticles it. Target queries are a physics sphere sweep, run only
/// on input (toggle/cycle), never per frame.
/// </summary>
[GlobalClass]
public partial class LockOnComponent : EntityComponent
{
    [Export] public float AcquireRange { get; set; } = 18f;

    /// <summary>A locked target is kept until it leaves this (larger) range.</summary>
    [Export] public float DropRange { get; set; } = 24f;

    private CharacterBody3D? _body;
    private int _team;

    public IEntity? Target { get; private set; }

    public bool IsLocked => Target != null;

    protected override void OnInitialize()
    {
        _body = Entity!.Body as CharacterBody3D;
        _team = Entity!.GetComponent<CombatComponent>()?.Team ?? 0;
    }

    /// <summary>Toggles lock: releases if already locked, otherwise locks the <paramref name="preferred"/>
    /// entity (the aimed-at focus) if it's a valid hostile, else the nearest hostile.</summary>
    public void Toggle(IEntity? preferred)
    {
        if (Target != null)
        {
            Target = null;
            return;
        }

        Target = IsValid(preferred) ? preferred : Nearest();
    }

    /// <summary>Switches to the next/previous nearby hostile.</summary>
    public void Cycle(int dir)
    {
        List<IEntity> targets = Acquire();
        if (targets.Count == 0)
        {
            Target = null;
            return;
        }

        int current = Target != null ? targets.IndexOf(Target) : -1;
        Target = targets[LockOn.CycleIndex(current, targets.Count, dir)];
    }

    /// <summary>Drops the target if it has died or left range. Cheap — call each frame.</summary>
    public void Tick()
    {
        if (Target != null && !IsValid(Target))
        {
            Target = null;
        }
    }

    /// <summary>
    /// While locked on, yaws the owner's body to face the target (look input only pitches). The
    /// level look — the target sampled at the body's own height — keeps it a pure yaw, so attacks
    /// and strafing orient at the foe rather than tilting the character at a taller or shorter one.
    ///
    /// <para>It lives here rather than in the player's input router because the rule is about the
    /// lock, not about the player: whoever holds a target faces it.</para>
    /// </summary>
    public void FaceTarget()
    {
        if (Target is not { } target || target.Body is not Node3D targetBody ||
            Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 to = targetBody.GlobalPosition - body.GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 0.01f)
        {
            return;
        }

        body.LookAt(
            new Vector3(targetBody.GlobalPosition.X, body.GlobalPosition.Y, targetBody.GlobalPosition.Z),
            Vector3.Up);
    }

    private IEntity? Nearest()
    {
        List<IEntity> targets = Acquire();
        return targets.Count > 0 ? targets[0] : null;
    }

    private List<IEntity> Acquire()
    {
        var result = new List<IEntity>();
        if (_body == null)
        {
            return result;
        }

        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = AcquireRange },
            Transform = new Transform3D(Basis.Identity, _body.GlobalPosition),
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };

        var seen = new HashSet<IEntity>();
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, maxResults: 32))
        {
            if (hit["collider"].AsGodotObject() is Node node
                && EntityNode.FindOwner(node) is { } entity
                && seen.Add(entity)
                && IsValid(entity))
            {
                result.Add(entity);
            }
        }

        // ⚠️ SCORED, NOT SORTED BY DISTANCE. The nearest valid enemy used to win outright, so
        // standing between two of them locked whichever was a hand's width closer and something
        // behind the player beat something they were looking straight at. LockOn.Score weights the
        // angle from where the player is actually looking three times as heavily as the distance.
        result.Sort((a, b) => ScoreOf(a).CompareTo(ScoreOf(b)));
        result.RemoveAll(e => ScoreOf(e) < 0f);
        return result;
    }

    /// <summary>The lock-on score for a candidate — lower is better, negative is not a candidate.
    /// Off-screen and behind-cover candidates are rejected here rather than being sorted to the
    /// back, because a target the player cannot see is never what they meant.</summary>
    private float ScoreOf(IEntity entity)
    {
        if (_body == null)
        {
            return -1f;
        }

        Vector3 to = entity.Body.GlobalPosition - _body.GlobalPosition;
        float distance = to.Length();
        Vector3 forward = Camera != null
            ? -Camera.GlobalTransform.Basis.Z
            : -_body.GlobalTransform.Basis.Z;

        float angle = distance <= 0.001f ? 0f : forward.AngleTo(to / distance);
        return LockOn.Score(distance, angle, AcquireRange, MaxAcquireAngle, HasLineOfSight(entity));
    }

    /// <summary>A candidate behind world geometry is not lockable. One ray, only for candidates that
    /// already passed the cheaper angle and range tests.</summary>
    private bool HasLineOfSight(IEntity entity)
    {
        if (_body == null)
        {
            return false;
        }

        Vector3 from = _body.GlobalPosition + (Vector3.Up * 1.4f);
        Vector3 to = entity.Body.GlobalPosition + (Vector3.Up * 1.0f);
        var query = PhysicsRayQueryParameters3D.Create(from, to, CombatLayers.World);
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
        return _body.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    /// <summary>Half-angle from the camera's forward a candidate may sit at, in radians. Roughly a
    /// 150-degree cone: generous enough to lock something at the edge of the screen, tight enough
    /// that nothing behind the player is ever a candidate.</summary>
    private const float MaxAcquireAngle = 1.3f;

    /// <summary>The player camera, so scoring can use where the player is LOOKING rather than where
    /// the body happens to face. Injected by the factory; null falls back to body facing.</summary>
    public Camera3D? Camera { get; set; }

    private float DistanceSq(IEntity entity) =>
        _body == null ? float.MaxValue : (entity.Body.GlobalPosition - _body.GlobalPosition).LengthSquared();

    private bool IsValid(IEntity? entity)
    {
        return entity is Node node
            && GodotObject.IsInstanceValid(node)
            && !ReferenceEquals(entity, Entity)
            && entity.GetComponent<CombatComponent>() is { } combat && combat.Team != _team
            && entity.GetComponent<StatsComponent>() is { IsAlive: true }
            && LockOn.InRange(DistanceSq(entity), DropRange * DropRange);
    }
}
