using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A damageable region attached under an entity. It is passive: it carries no
/// logic beyond pointing back at its owner's <see cref="CombatComponent"/>, so a
/// <see cref="Hitbox"/> that overlaps it can deliver a <see cref="DamagePacket"/>.
/// Add a <c>CollisionShape3D</c> child to define its volume.
/// </summary>
[GlobalClass]
public partial class Hurtbox : Area3D
{
    public IEntity? OwnerEntity { get; private set; }

    public CombatComponent? Combat { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = CombatLayers.Hurtbox;
        CollisionMask = 0;
        Monitorable = true;
        Monitoring = false;

        OwnerEntity = EntityNode.FindOwner(this);
        Combat = OwnerEntity?.GetComponent<CombatComponent>();
    }

    /// <summary>Delivers a hit to the owning combat component, if any.</summary>
    public DamageResult Receive(DamagePacket packet)
    {
        return Combat?.ReceiveDamage(packet) ?? default;
    }
}
