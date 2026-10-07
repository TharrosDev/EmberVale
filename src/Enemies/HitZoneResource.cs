using Godot;

namespace Embervale.Enemies;

/// <summary>
/// One damageable region of a multi-zone body (Phase 35A) — a dragon's head, wing, flank or tail —
/// authored as a sub-resource inside an <see cref="EnemyArchetypeResource"/>'s <c>HitZones</c>, the
/// same way <c>LootEntry</c> is authored inside a loot table.
///
/// <see cref="EnemyArchetypeFactory"/> turns each one into a <see cref="Combat.Hurtbox"/> parented to
/// the actor. An archetype with no zones keeps the single whole-body capsule every enemy before this
/// phase had, so this is purely additive.
/// </summary>
[GlobalClass]
public partial class HitZoneResource : Resource
{
    /// <summary>Zone name, e.g. <c>head</c>. Unique within one archetype; names the node too.</summary>
    [Export] public string Id { get; set; } = "body";

    /// <summary>Incoming damage (and poise damage) is multiplied by this. 2 = a weak point.</summary>
    [Export] public float DamageMultiplier { get; set; } = 1f;

    /// <summary>Extra poise-damage scale on top of <see cref="DamageMultiplier"/>. 1 = poise follows
    /// damage; above 1 the zone is easy to unbalance (a wing joint), below 1 hard to (a horned brow).</summary>
    [Export] public float PoiseMultiplier { get; set; } = 1f;

    /// <summary>Where the zone sits relative to the actor's origin (its feet), in metres. Negative Z
    /// is forward — the same glTF→Godot convention the factories' hitbox offsets use.</summary>
    [Export] public Vector3 Offset { get; set; } = Vector3.Zero;

    [Export] public float Radius { get; set; } = 0.5f;

    /// <summary>Capsule height. Zero (or below twice the radius) makes it a sphere instead — the
    /// right shape for a head or a wing knuckle.</summary>
    [Export] public float Height { get; set; }

    /// <summary>Turns the capsule off the vertical, in degrees. Zero, the default, is the upright
    /// capsule every zone had before the dragons lay down: a tail runs back along the ground and a
    /// pair of wings runs side to side, and an upright capsule fits neither. X -90 lays it along
    /// the body's forward axis; Z 90 lays it across the body. A sphere ignores it.</summary>
    [Export] public Vector3 RotationDegrees { get; set; } = Vector3.Zero;
}
