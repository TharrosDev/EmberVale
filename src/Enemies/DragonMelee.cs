namespace Embervale.Enemies;

/// <summary>Which of a big body's melee attacks fires (Phase 35A).</summary>
public enum DragonAttack
{
    /// <summary>Straight ahead — the jaws.</summary>
    Bite,

    /// <summary>Off to either side — a wing sweep across the flank.</summary>
    Wing,

    /// <summary>Behind — the tail comes round.</summary>
    Tail,
}

/// <summary>
/// The pure attack-selection brain for a multi-zone body (Phase 35A), in the shape of
/// <see cref="PackFlank"/> and <see cref="CasterDecision"/>: Godot-free, so the angles are unit-
/// testable apart from the hitboxes they drive.
///
/// A dragon that only ever bit forwards would be beaten by standing behind it — the whole point of a
/// body this large is that every side of it is dangerous. Bearing is the signed angle from the
/// dragon's facing to the target, so the choice is: in front, bite; to a flank, wing; behind, tail.
/// </summary>
public static class DragonMelee
{
    /// <summary>Half-width of the frontal cone the bite covers, in degrees.</summary>
    public const float BiteHalfAngle = 50f;

    /// <summary>Beyond this the target is behind the dragon and the tail answers instead.</summary>
    public const float WingHalfAngle = 130f;

    /// <summary>
    /// Picks the attack for a target at <paramref name="bearingDegrees"/> off the dragon's facing
    /// (0 = dead ahead, ±180 = directly behind; sign is ignored, the body is symmetric).
    /// </summary>
    public static DragonAttack Choose(float bearingDegrees)
    {
        float bearing = bearingDegrees < 0f ? -bearingDegrees : bearingDegrees;
        if (bearing <= BiteHalfAngle)
        {
            return DragonAttack.Bite;
        }

        return bearing <= WingHalfAngle ? DragonAttack.Wing : DragonAttack.Tail;
    }
}
