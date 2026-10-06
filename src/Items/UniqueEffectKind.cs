namespace Embervale.Items;

/// <summary>
/// The closed set of behaviours a <see cref="UniqueEffectResource"/> can have. Each one is a single
/// hook that item and combat code implements once; a named legendary or a set bonus picks a kind and
/// supplies numbers. Which numeric field means what is listed per member, and a field a kind does
/// not name is ignored for it.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum UniqueEffectKind
{
    /// <summary>A landed hit has <c>Chance</c> to apply <c>StatusId</c> to the target (burn, chill, ...).</summary>
    OnHitStatus,

    /// <summary>A kill heals the wearer for <c>Magnitude</c> (a fraction of max health, 0..1).</summary>
    OnKillHeal,

    /// <summary>While health is below <c>Threshold</c> (0..1), damage dealt is raised by <c>Magnitude</c> (fraction).</summary>
    LowHealthPower,

    /// <summary>A successful block returns <c>Magnitude</c> (fraction) of the blocked damage to the attacker.</summary>
    BlockReflect,

    /// <summary>A spell cast has <c>Chance</c> to repeat at <c>Magnitude</c> (fraction) power; <c>CooldownSeconds</c> between echoes.</summary>
    SpellEcho,

    /// <summary>A dodge that avoids a hit refunds <c>Magnitude</c> stamina.</summary>
    DodgeRefund,

    /// <summary>Gold picked up is raised by <c>Magnitude</c> (fraction).</summary>
    GoldFind,

    /// <summary>A melee attacker takes <c>Magnitude</c> flat damage when it hits the wearer.</summary>
    ThornsFlat,

    /// <summary>A critical hit on a target below <c>Threshold</c> (0..1) health deals <c>Magnitude</c> (fraction) more.</summary>
    CritExecute,

    /// <summary><c>Magnitude</c> (fraction) of damage taken is paid from mana while any remains.</summary>
    ManaShield,
}
