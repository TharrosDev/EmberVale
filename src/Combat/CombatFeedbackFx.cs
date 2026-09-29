using System;

namespace Embervale.Combat;

/// <summary>The player combat states that get distinct screen feedback (Phase 29D). <b>Append-only.</b></summary>
public enum CombatFeedback
{
    Crit,
    Block,
    Stagger,
    Parry,

    /// <summary>The player's guard gave out.</summary>
    GuardBroken,

    /// <summary>The player broke an enemy's poise: the opening to press.</summary>
    Break,

    /// <summary>The player landed a riposte on a parried or staggered target.</summary>
    Riposte,

    /// <summary>The player struck from behind.</summary>
    Backstab,
}

/// <summary>
/// Pure mapping from a player combat state to its screen-flash tint + intensity (Phase 29D). Godot-free
/// so it's unit-testable; <see cref="Embervale.UI.CombatFeedbackOverlay"/> turns it into a fading
/// full-screen flash. Tints follow the dying-world UI identity (30.5E, docs/UI_STYLE.md §1): crit is
/// THE ember-orange accent, block is cold steel, stagger ashen red, parry a bright ember-gold pop.
/// Knobs live here.
/// </summary>
public static class CombatFeedbackFx
{
    public const float HoldSeconds = 0.28f;

    /// <summary>The least time between two screen flashes of the same or lower priority. Three a second
    /// is the photosensitivity ceiling, so a run of blocks or a flurry of crits cannot strobe; a
    /// stronger state (a parry, a guard break) is never held back by a weaker one.</summary>
    public const float MinFlashInterval = 0.33f;

    /// <summary>Flash tint (r,g,b 0..1) for a state.</summary>
    public static (float R, float G, float B) Tint(CombatFeedback state) => state switch
    {
        CombatFeedback.Crit => (0.91f, 0.45f, 0.17f),    // ember orange (UiTheme.AccentHot)
        CombatFeedback.Block => (0.49f, 0.53f, 0.57f),   // cold steel (ART_STYLE palette)
        CombatFeedback.Stagger => (0.82f, 0.42f, 0.36f), // ashen red (UiTheme.Bad)
        CombatFeedback.Parry => (1.0f, 0.88f, 0.60f),    // bright ember-gold pop
        CombatFeedback.GuardBroken => (0.62f, 0.16f, 0.14f), // dried-blood red, darker than a stagger
        CombatFeedback.Break => (0.85f, 0.72f, 0.42f),   // pale brass
        CombatFeedback.Riposte => (1.0f, 0.72f, 0.28f),  // hot gold
        CombatFeedback.Backstab => (0.72f, 0.60f, 0.86f), // cold violet dusk
        _ => (1f, 1f, 1f),
    };

    /// <summary>Peak flash alpha for a state — a parry/crit pops harder than a routine block.</summary>
    public static float PeakAlpha(CombatFeedback state) => state switch
    {
        CombatFeedback.Parry => 0.45f,
        CombatFeedback.GuardBroken => 0.50f,
        CombatFeedback.Crit => 0.32f,
        CombatFeedback.Riposte => 0.36f,
        CombatFeedback.Backstab => 0.30f,
        CombatFeedback.Stagger => 0.40f,
        CombatFeedback.Break => 0.18f,
        CombatFeedback.Block => 0.22f,
        _ => 0.25f,
    };

    /// <summary>How long the word and flash hold. A guard break lingers, a block barely does.</summary>
    public static float Hold(CombatFeedback state) => state switch
    {
        CombatFeedback.GuardBroken => 0.42f,
        CombatFeedback.Parry => 0.34f,
        CombatFeedback.Block => 0.2f,
        _ => HoldSeconds,
    };

    /// <summary>Which state wins when two land close together: the higher one is never suppressed by
    /// the flash interval and replaces the lower one on screen.</summary>
    public static int Priority(CombatFeedback state) => state switch
    {
        CombatFeedback.GuardBroken => 6,
        CombatFeedback.Parry => 5,
        CombatFeedback.Stagger => 4,
        CombatFeedback.Riposte => 3,
        CombatFeedback.Crit => 3,
        CombatFeedback.Backstab => 3,
        CombatFeedback.Break => 2,
        _ => 1,
    };

    /// <summary>The locale key of the word shown for a state.</summary>
    public static string WordKey(CombatFeedback state) => state switch
    {
        CombatFeedback.Crit => "feedback.crit",
        CombatFeedback.Block => "feedback.block",
        CombatFeedback.Stagger => "feedback.stagger",
        CombatFeedback.Parry => "feedback.parry",
        CombatFeedback.GuardBroken => "combat.feedback.guard_broken",
        CombatFeedback.Break => "combat.feedback.broken",
        CombatFeedback.Riposte => "combat.feedback.riposte",
        CombatFeedback.Backstab => "combat.feedback.backstab",
        _ => "feedback.crit",
    };

    /// <summary>The flash alpha the player has asked for: the state's peak scaled by the combat flash
    /// setting (which Reduced Motion has already capped). A scale of 0 leaves the word and drops the
    /// flash.</summary>
    public static float FlashAlpha(CombatFeedback state, float flashScale) =>
        PeakAlpha(state) * Math.Clamp(flashScale, 0f, 1f);

    /// <summary>
    /// The state the player's own outcomes map to, or null when the outcome earns no screen word.
    /// <paramref name="byPlayer"/>: the player dealt it; <paramref name="onPlayer"/>: the player took
    /// it. A parry is the player's when they were the defender. Ordinary hits earn nothing — the
    /// number and the spark already say so.
    /// </summary>
    public static CombatFeedback? ForOutcome(
        HitOutcome outcome, HitKind kind, bool staggered, bool byPlayer, bool onPlayer)
    {
        switch (outcome)
        {
            case HitOutcome.Parried:
                // The defender parried: the player's when they raised the guard; when the player was
                // the attacker they were the one staggered by it.
                return onPlayer ? CombatFeedback.Parry : byPlayer ? CombatFeedback.Stagger : null;
            case HitOutcome.GuardBroken:
                return onPlayer ? CombatFeedback.GuardBroken : byPlayer ? CombatFeedback.Break : null;
            case HitOutcome.Critical:
                if (byPlayer)
                {
                    return kind switch
                    {
                        HitKind.Riposte => CombatFeedback.Riposte,
                        HitKind.Backstab => CombatFeedback.Backstab,
                        _ => CombatFeedback.Crit,
                    };
                }

                return onPlayer && staggered ? CombatFeedback.Stagger : null;
            case HitOutcome.PoiseBroken:
                return onPlayer ? CombatFeedback.Stagger : byPlayer ? CombatFeedback.Break : null;
            case HitOutcome.Blocked:
                return onPlayer ? CombatFeedback.Block : null;
            default:
                return onPlayer && staggered ? CombatFeedback.Stagger : null;
        }
    }
}

/// <summary>The clock behind <see cref="CombatFeedbackFx.MinFlashInterval"/>: decides whether a new
/// flash may fire, given what fired last. Pure, clocked by the caller.</summary>
public sealed class FlashGate
{
    private double _lastAt = double.NegativeInfinity;
    private int _lastPriority;

    /// <summary>Whether a flash of <paramref name="state"/> may fire at <paramref name="nowSeconds"/>.
    /// A higher priority than the last always may; an equal or lower one waits out the interval.</summary>
    public bool Allow(double nowSeconds, CombatFeedback state)
    {
        int priority = CombatFeedbackFx.Priority(state);
        bool allowed = nowSeconds - _lastAt >= CombatFeedbackFx.MinFlashInterval ||
                       priority > _lastPriority;
        if (allowed)
        {
            _lastAt = nowSeconds;
            _lastPriority = priority;
        }

        return allowed;
    }

    /// <summary>Forgets the last flash (a load, a scene change).</summary>
    public void Reset()
    {
        _lastAt = double.NegativeInfinity;
        _lastPriority = 0;
    }
}
