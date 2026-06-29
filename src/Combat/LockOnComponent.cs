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

        result.Sort((a, b) => DistanceSq(a).CompareTo(DistanceSq(b)));
        return result;
    }

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
