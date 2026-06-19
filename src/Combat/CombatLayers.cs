namespace Embervale.Combat;

/// <summary>
/// Physics collision-layer bit assignments used across the project. Centralized
/// so layers stay consistent between bodies, hitboxes and hurtboxes. Values are
/// raw masks suitable for <c>CollisionLayer</c>/<c>CollisionMask</c>.
/// </summary>
public static class CombatLayers
{
    /// <summary>Static world geometry (ground, props).</summary>
    public const uint World = 1u << 0;

    /// <summary>Solid actor bodies (CharacterBody3D / blocking colliders).</summary>
    public const uint Body = 1u << 1;

    /// <summary>Damageable regions (<see cref="Hurtbox"/>).</summary>
    public const uint Hurtbox = 1u << 2;

    /// <summary>Damage-dealing regions (<see cref="Hitbox"/>).</summary>
    public const uint Hitbox = 1u << 3;
}
