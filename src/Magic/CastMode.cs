namespace Embervale.Magic;

/// <summary>
/// How a <see cref="SpellResource"/> is cast over time (Phase 29.5A), layered on top of the
/// <see cref="SpellDelivery"/> *shape*. Instant is fire-and-forget (the original behaviour); Charged
/// builds power while the cast key is held and fires on release; Channeled sustains a per-tick effect at a
/// mana-per-second cost until released or interrupted.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum CastMode
{
    /// <summary>Cast immediately on press (the default — every pre-29.5 spell).</summary>
    Instant,

    /// <summary>Hold to charge (power scales with hold), release to fire.</summary>
    Charged,

    /// <summary>Hold to sustain a repeating effect, paying mana per second; release to stop.</summary>
    Channeled,
}
