using Godot;

namespace Embervale.Animation;

/// <summary>
/// How a weapon sits in a hand.
///
/// <para>Weapons in this repo are authored to one contract (<c>docs/3D_ASSETS.md</c> → FIRST-PERSON
/// / VIEWMODEL): the functional long axis is local <b>+Y</b> — grip to point — <b>+Z</b> is the face,
/// <b>+X</b> the wielder's right, and the origin sits on the centreline of the grip. A retargeted
/// hand bone's own basis does not point that way, so a weapon parented straight to it lies across
/// the palm.</para>
///
/// <para>⚠️ <b>This correction belongs here and not inside a weapon's <c>.glb</c>.</b> The asset
/// contract is explicit that a second compensating transform must never be baked into the mesh: the
/// same model is used by the world body and by the first-person view, and a mesh rotated to suit one
/// of them is wrong in the other. One correction, at the socket, for every wielder in the game.</para>
/// </summary>
public static class WeaponGrip
{
    /// <summary>
    /// The basis that maps a weapon's canonical <c>+Y</c> long axis up, slightly outward and
    /// slightly forward out of the fist.
    ///
    /// Carried over verbatim from <c>PlayerFactory.AttachWeaponVisual</c>, where it was measured
    /// against the player's rig and then lived alone in a private method no other actor could reach —
    /// which is why every companion, NPC and enemy that carried a weapon carried it unrotated.
    /// </summary>
    public static Basis Hand
    {
        get
        {
            Vector3 blade = new Vector3(-0.30f, 0.25f, -0.90f).Normalized();
            Vector3 across = blade.Cross(Vector3.Up).Normalized();
            Vector3 face = across.Cross(blade);
            return new Basis(across, blade, face);
        }
    }

    /// <summary>
    /// <see cref="Hand"/> as the degrees a <see cref="Node3D"/> wants.
    ///
    /// ⚠️ <c>Basis.GetEuler</c> and <c>Node3D.RotationDegrees</c> both default to <c>YXZ</c>, so this
    /// round-trips exactly. It is derived rather than written down as three literals precisely so it
    /// cannot drift from the basis above.
    /// </summary>
    public static Vector3 HandRotationDegrees => Hand.GetEuler() * (180f / Mathf.Pi);
}
