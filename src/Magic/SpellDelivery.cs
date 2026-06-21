namespace Embervale.Magic;

/// <summary>
/// How a <see cref="SpellResource"/> reaches its target(s). Kept deliberately small
/// — these three shapes cover the common cases (a travelling bolt, a burst around
/// the caster, and a self-cast buff/heal) and richer shapes can be appended later
/// without touching the cast flow.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum SpellDelivery
{
    /// <summary>A bolt that travels forward and resolves on impact (single-target,
    /// or an area burst when the spell carries an impact radius).</summary>
    Projectile,

    /// <summary>An instant area-of-effect burst centred on the caster.</summary>
    Area,

    /// <summary>Affects only the caster — a heal and/or a self-applied buff.</summary>
    Self,
}
