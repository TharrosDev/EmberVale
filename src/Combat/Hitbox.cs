using System.Collections.Generic;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A damage-dealing region (a weapon swing arc, later a spell or projectile). It
/// is inert until <see cref="Activate"/> opens its window, during which it polls
/// for overlapping <see cref="Hurtbox"/>es each physics frame and delivers its
/// <see cref="DamagePacket"/> once per target. Polling (rather than relying on
/// area-entered signal timing) makes hits reliable across the short active window.
/// Add a <c>CollisionShape3D</c> child to define its volume.
/// </summary>
[GlobalClass]
public partial class Hitbox : Area3D
{
    // Keyed on the owning entity, not the hurtbox — a multi-zone body (35A) is still one target.
    private readonly HitDedupe _alreadyHit = new();
    private IEntity? _ownerEntity;
    private int _ownerTeam;
    private DamagePacket _packet;
    private bool _active;

    public override void _Ready()
    {
        CollisionLayer = CombatLayers.Hitbox;
        CollisionMask = CombatLayers.Hurtbox;
        Monitorable = false;
        Monitoring = false;

        // Inert means inert: without this every hitbox in the world (one per melee actor, three on
        // the dragon) pays a managed _PhysicsProcess dispatch 60 times a second to return on its
        // first line. Activate/Deactivate are the only two things that change the answer.
        SetPhysicsProcess(false);

        _ownerEntity = EntityNode.FindOwner(this);
        _ownerTeam = _ownerEntity?.GetComponent<CombatComponent>()?.Team ?? 0;
    }

    /// <summary>Opens the damage window with the given packet, clearing prior hits.</summary>
    public void Activate(DamagePacket packet)
    {
        _packet = packet;
        _alreadyHit.Clear();
        _active = true;
        Monitoring = true;
        SetPhysicsProcess(true);
    }

    /// <summary>Closes the damage window.</summary>
    public void Deactivate()
    {
        _active = false;
        Monitoring = false;
        SetPhysicsProcess(false);
        _alreadyHit.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active)
        {
            return;
        }

        foreach (Area3D area in GetOverlappingAreas())
        {
            if (area is not Hurtbox hurtbox)
            {
                continue;
            }

            // Never hit our own hurtbox.
            if (hurtbox.OwnerEntity != null && ReferenceEquals(hurtbox.OwnerEntity, _ownerEntity))
            {
                continue;
            }

            // Skip allies on the same team (friendly fire off).
            if (hurtbox.Combat != null && hurtbox.Combat.Team == _ownerTeam)
            {
                continue;
            }

            // Last, so an ally/self skip above never burns the owner's one hit for this swing.
            if (!_alreadyHit.TryHit(hurtbox.OwnerEntity, hurtbox))
            {
                continue;
            }

            hurtbox.Receive(_packet);
        }
    }
}
