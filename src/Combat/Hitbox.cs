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
    private readonly HashSet<Hurtbox> _alreadyHit = new();
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
    }

    /// <summary>Closes the damage window.</summary>
    public void Deactivate()
    {
        _active = false;
        Monitoring = false;
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
            if (area is not Hurtbox hurtbox || _alreadyHit.Contains(hurtbox))
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

            _alreadyHit.Add(hurtbox);
            hurtbox.Receive(_packet);
        }
    }
}
