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

    /// <summary>A wedge sweeping out from the caster along their aim (Phase 35C, dragon breath):
    /// everything inside <see cref="SpellResource.ConeAngleDegrees"/> and within
    /// <see cref="SpellResource.ImpactRadius"/> of the origin. An Area burst you have to be in
    /// front of.</summary>
    Cone,

    // --- magic upgrade 2026-09 (append-only) ---

    /// <summary>Lands where the caster aims, after <see cref="SpellResource.GroundDelay"/> seconds of
    /// telegraph on the ground (Sunfall, Gravity Well, Thornsnare). The delay is the warning: it is
    /// the same ring an enemy's wind-up draws, so a ground spell can be read and dodged.</summary>
    Ground,

    /// <summary>A standing wall across the aim point that lasts <see cref="SpellResource.BarrierDuration"/>
    /// (Pyre Wall, Glacial Bulwark). It can stop projectiles and bodies and can be broken.</summary>
    Barrier,

    /// <summary>The caster travels <see cref="SpellResource.DashDistance"/> along the aim and strikes
    /// what it passes through (Thunder Step). A Self cast that moves; Blink stays a teleport.</summary>
    Dash,
}
