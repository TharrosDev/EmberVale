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

    /// <summary>
    /// Geometry the third-person camera may not pass through.
    ///
    /// ⚠️ <b>A separate layer from <see cref="World"/>, and that separation is the whole point.</b>
    /// The camera spring used to sweep against World, which actor bodies also occupy — so a
    /// companion walking between the player and the camera yanked the camera in, and a
    /// <c>ponytail:</c> note in <c>PlayerCameraRig</c> admitted it and left it. Walls block the
    /// camera; people do not. Anything that wants to block it says so by being on this layer.
    /// </summary>
    public const uint CameraBlocker = 1u << 4;
}
