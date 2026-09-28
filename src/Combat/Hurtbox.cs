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

    /// <summary>Scales poise damage on top of <see cref="DamageMultiplier"/> (which poise already
    /// follows), so a zone can be tender to stagger without being tender to steel: a wing that is
    /// hard to hurt but easy to unbalance. Default <c>1</c>.</summary>
    [Export]
    public float PoiseMultiplier { get; set; } = 1f;

    /// <summary>A zone that takes at least half again the damage is a weak point: a head, a
    /// throat, a cracked plate. Ranged attackers and presentation read this rather than a zone name.</summary>
    public bool IsWeakPoint => DamageMultiplier >= 1.5f;

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

        if (DamageMultiplier != 1f || PoiseMultiplier != 1f)
        {
            float damage = Mathf.Max(0f, DamageMultiplier);
            packet = packet with
            {
                Amount = packet.Amount * damage,
                PoiseDamage = packet.PoiseDamage * damage * Mathf.Max(0f, PoiseMultiplier),
            };
        }

        return Combat.ReceiveDamage(packet);
    }
}
