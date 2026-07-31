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
    /// <summary>Which body zone this is (<c>head</c>, <c>tail</c>, …) on a multi-zone actor, or empty
    /// for the usual whole-body hurtbox. Phase 35A; diagnostic/authoring only.</summary>
    [Export]
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>Scales incoming damage — a dragon's head takes double, its tail shrugs hits off. The
    /// default <c>1</c> leaves every pre-35A actor's damage untouched.</summary>
    [Export]
    public float DamageMultiplier { get; set; } = 1f;

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

    /// <summary>Delivers a hit to the owning combat component, if any, scaled by this zone's
    /// multiplier. Poise scales with it too, so a headshot staggers harder than a tail clip.</summary>
    public DamageResult Receive(DamagePacket packet)
    {
        if (Combat == null)
        {
            return default;
        }

        if (DamageMultiplier != 1f)
        {
            packet = packet with
            {
                Amount = packet.Amount * DamageMultiplier,
                PoiseDamage = packet.PoiseDamage * DamageMultiplier,
            };
        }

        return Combat.ReceiveDamage(packet);
    }
}
