namespace Embervale.Combat;

/// <summary>
/// Physics collision-layer bit assignments used across the project. Centralized
/// so layers stay consistent between bodies, hitboxes and hurtboxes. Values are
/// raw masks suitable for <c>CollisionLayer</c>/<c>CollisionMask</c>.
/// </summary>
public static class CombatLayers
{
    /// <summary>Static world geometry (authoritative terrain, walls and solid props).</summary>
    public const uint WorldStatic = 1u << 0;

    /// <summary>Movable/kinematic world bodies and the legacy solid actor layer.</summary>
    public const uint WorldDynamic = 1u << 1;

    /// <summary>Damageable regions (<see cref="Hurtbox"/>).</summary>
    public const uint Hurtbox = 1u << 2;

    /// <summary>Damage-dealing regions (<see cref="Hitbox"/>).</summary>
    public const uint Hitbox = 1u << 3;

    public const uint Player = 1u << 4;
    public const uint Enemy = 1u << 5;
    public const uint Npc = 1u << 6;
    public const uint Projectile = 1u << 7;
    public const uint Interaction = 1u << 8;
    public const uint CameraBlocker = 1u << 9;
    public const uint NavigationObstacle = 1u << 10;
    public const uint Water = 1u << 11;
    public const uint Trigger = 1u << 12;
    public const uint Ragdoll = 1u << 13;

    /// <summary>Compatibility names used by existing authored content.</summary>
    public const uint World = WorldStatic;
    public const uint Body = WorldDynamic;

    public const uint PhysicalWorld = WorldStatic | WorldDynamic;
    public const uint CameraObstruction = WorldStatic | CameraBlocker;

    /// <summary>Areas must never block motion even when they observe these bodies.</summary>
    public static bool IsSensorLayer(uint layer) =>
        (layer & (Hurtbox | Hitbox | Interaction | Trigger | Water)) != 0u;

    /// <summary>The canonical default mask for a projectile that participates in combat.</summary>
    public const uint ProjectileMask = WorldStatic | WorldDynamic | Hurtbox;
}
