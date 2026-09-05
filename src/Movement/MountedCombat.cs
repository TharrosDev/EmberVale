namespace Embervale.Movement;

/// <summary>
/// What being on a horse does to a blow (Phase 39B) — pure, Godot-free, and unit-tested, because
/// <see cref="Combat.CharacterActionComponent"/> asks it on <b>every</b> swing in the game and a wrong
/// answer for an actor with no mount would be a silent, world-wide damage change.
///
/// <b>The rule is the gait, not the mount.</b> Sitting still on a horse is worth nothing — the
/// weight is only a weapon when it is moving, so a walking mount is exactly neutral and the bonus
/// rides on the gallop. That is deliberate: it is what makes 39A's gallop pool a decision inside a
/// fight instead of a commuting stat. Spending the pool to open a fight hard means walking out of it.
///
/// ⚠️ <b>What this does NOT do is move the hitbox.</b> The swing volume hangs off the body capsule,
/// not off the raised <c>BodyMesh</c>, so mounted reach is unchanged while the visible sword sits
/// 0.86 m higher. Chasing that with a shared hitbox is a bigger change than the mismatch is worth —
/// named here so the next reader knows it was seen rather than missed.
/// </summary>
public static class MountedCombat
{
    /// <summary>A mount standing or walking. Weight with no speed behind it changes nothing.</summary>
    public const float WalkingScale = 1f;

    /// <summary>A gallop — the charge. ⚠️ Phase 56 owns this number; it is the first authored value.</summary>
    public const float GallopScale = 1.45f;

    /// <summary>
    /// The multiplier on a melee blow's base damage.
    ///
    /// ⚠️ <b>An unmounted attacker must return exactly 1.0</b>, not approximately — every enemy,
    /// companion and the player on foot route through here, and a 0.99 would quietly restat the
    /// entire game's melee. The unmounted case is therefore the first branch and a literal.
    /// </summary>
    public static float DamageScale(bool mounted, bool galloping)
    {
        if (!mounted)
        {
            return 1f;
        }

        return galloping ? GallopScale : WalkingScale;
    }
}
