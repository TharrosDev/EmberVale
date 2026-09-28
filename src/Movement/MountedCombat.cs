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
/// Since the gait upgrade "galloping" is <see cref="IsCharging"/> — granted <em>and</em> moving at
/// gallop pace — so a trot, a canter and a gallop that has not gathered speed all swing for 1.0.
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

    /// <summary>How close to full gallop pace the horse must actually be moving for a blow to count
    /// as the charge. ⚠️ Phase 56 owns this number.</summary>
    public const float ChargeSpeedFraction = 0.8f;

    /// <summary>
    /// Whether a galloping horse is actually delivering a charge — the tightening of 39B's rule.
    ///
    /// ⚠️ <b>"GALLOPING" USED TO MEAN "THE POOL GRANTED A GALLOP", AND THE POOL DOES NOT KNOW THE HORSE
    /// IS MOVING.</b> A rider pinned against a wall with sprint held was galloping by that reading, and
    /// swung for the full charge from a standstill; so was the first frame of a gallop, before the horse
    /// had gathered any pace at all. The charge is the horse's weight <em>at speed</em>, so it is
    /// measured on the body's real horizontal speed against the gallop's, in the same units.
    /// </summary>
    /// <param name="gallopGranted">The pool's answer (<see cref="MountRules.GallopState.Galloping"/>).</param>
    /// <param name="speed">Measured horizontal speed.</param>
    /// <param name="gallopSpeed">The gallop's settled speed, in the same units as <paramref name="speed"/>.</param>
    public static bool IsCharging(bool gallopGranted, float speed, float gallopSpeed) =>
        gallopGranted && gallopSpeed > 0f && float.IsFinite(speed) &&
        speed >= gallopSpeed * ChargeSpeedFraction;
}
