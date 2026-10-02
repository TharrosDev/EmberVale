using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Magic;

/// <summary>Raised when an entity successfully casts a spell (mana spent, effect launched).</summary>
public readonly record struct SpellCastEvent(IEntity Caster, string SpellId) : IGameEvent;

/// <summary>Raised when a caster changes its selected/prepared spell.</summary>
public readonly record struct SpellSelectedEvent(IEntity Caster, string SpellId) : IGameEvent;

/// <summary>Raised when a caster's known spells or ranks change (bought/upgraded), so UI can refresh.</summary>
public readonly record struct SpellsChangedEvent(IEntity Caster) : IGameEvent;

/// <summary>Raised when a status effect is first applied to an entity (not on refresh).</summary>
public readonly record struct StatusEffectAppliedEvent(IEntity Target, string EffectId, IEntity? Source) : IGameEvent;

/// <summary>Raised when a status effect expires or is cleared from an entity.</summary>
public readonly record struct StatusEffectRemovedEvent(IEntity Target, string EffectId) : IGameEvent;

// --- magic contract events (committed on magic/integration; each agent appends only inside its own block) ---

/// <summary>A cast began its visible wind-up. <paramref name="WindupSeconds"/> is the effective time,
/// so a telegraph and an AI's read of the cast run off the same number.</summary>
public readonly record struct CastWindupStartedEvent(IEntity Caster, string SpellId, float WindupSeconds) : IGameEvent;

/// <summary>A cast was cancelled before release: a stagger, a silence, a stun, or the caster dying.</summary>
public readonly record struct SpellInterruptedEvent(IEntity Caster, string SpellId, IEntity? Interrupter) : IGameEvent;

/// <summary>A spell was stopped by a guard (spells are blockable, never parryable) or by a barrier.</summary>
public readonly record struct SpellBlockedEvent(IEntity Caster, string SpellId, IEntity? Blocker) : IGameEvent;

/// <summary>A spell landed on a target, after mitigation. Carries the resolved amount so presentation
/// and mastery do not recompute it.</summary>
public readonly record struct SpellHitEvent(
    IEntity Caster, IEntity Target, string SpellId, float Amount, bool IsCrit) : IGameEvent;

/// <summary>A barrier or placed spell finished: <paramref name="Broken"/> is true when it was destroyed
/// rather than expiring.</summary>
public readonly record struct BarrierEndedEvent(IEntity? Caster, string SpellId, bool Broken) : IGameEvent;

/// <summary>A status was stripped by a dispel or cleanse rather than expiring.</summary>
public readonly record struct StatusDispelledEvent(IEntity Target, string EffectId, IEntity? Source) : IGameEvent;

/// <summary>A stacking status detonated (Kindle at full stacks) or a combo consumed one.</summary>
public readonly record struct StatusDetonatedEvent(IEntity Target, string EffectId, float Damage, IEntity? Source) : IGameEvent;

/// <summary>A control status was refused because the bearer is still immune from the last one.</summary>
public readonly record struct StatusResistedEvent(IEntity Target, string EffectId) : IGameEvent;

/// <summary>A school combo fired. <paramref name="ComboId"/> is stable.</summary>
public readonly record struct SpellComboEvent(IEntity Source, IEntity Target, string ComboId) : IGameEvent;

/// <summary>The player learned a spell by any route (tome, dialogue, trainer). Fires once and is
/// suppressed on the load path: a load restores knowledge, it does not narrate it.</summary>
public readonly record struct SpellLearnedEvent(IEntity Caster, string SpellId, string Route) : IGameEvent;

/// <summary>A school gained a mastery rank.</summary>
public readonly record struct SchoolRankedUpEvent(IEntity Caster, Embervale.Combat.DamageType School, int Rank) : IGameEvent;

// --- magic-core events: append inside this block only ---

/// <summary>A spell landed, with what presentation weighs it by: the spell's authored
/// <c>ImpactWeight</c> (0..1) and the charge (0..1) a held cast reached. Raised beside
/// <see cref="SpellHitEvent"/> by the casting core; hit-stop and shake read it through the combat
/// feedback director and never feed back into a rule.</summary>
public readonly record struct SpellImpactEvent(
    IEntity Caster, IEntity Target, string SpellId, float Weight, float Charge) : IGameEvent;

// --- end magic-core events ---
// --- magic-status events: append inside this block only ---

/// <summary>A ward absorbed its last point and broke with a flash. An expiring ward raises only
/// <see cref="StatusEffectRemovedEvent"/>.</summary>
public readonly record struct WardBrokenEvent(IEntity Target, string EffectId) : IGameEvent;
// --- end magic-status events ---
// --- magic-learning events: append inside this block only ---

/// <summary>Why a learn attempt was refused, so the player is told rather than left guessing.</summary>
public enum SpellLearnRefusal
{
    /// <summary>The learner already has the spell.</summary>
    AlreadyKnown,

    /// <summary>The spell is corrupted and the learner's tier is below its <c>MinCorruptionTier</c>.</summary>
    CorruptionTooLow,

    /// <summary>A tome is sealed behind a story flag.</summary>
    Sealed,

    /// <summary>A corrupted spell is within reach: the learner must confirm the choice (interact again).</summary>
    ConfirmCorrupted,
}

/// <summary>A learn attempt did not teach anything. <paramref name="TierNow"/> and
/// <paramref name="TierRequired"/> are <c>CorruptionTier</c> ordinals (0 when not relevant). Presentation
/// only: the toast feed turns it into a legible line.</summary>
public readonly record struct SpellLearnRefusedEvent(
    IEntity Learner, string SpellId, SpellLearnRefusal Reason, int TierNow, int TierRequired) : IGameEvent;

// --- end magic-learning events ---
// --- magic-content events: append inside this block only ---
// --- end magic-content events ---
