using System;

namespace Embervale.Combat;

/// <summary>
/// Pure tuning maths for <b>hit-stop</b> (Phase 29A): how long the brief freeze-frame on a landed hit
/// lasts, in milliseconds, as a function of the hit's weight. Kept Godot-free so the feel curve is
/// unit-testable without an engine; <see cref="HitStopDirector"/> applies it against
/// <c>Engine.TimeScale</c>. All knobs live here — tweak and re-run to retune.
/// </summary>
public static class HitStop
{
    /// <summary>Damage at/above which a hit counts as fully "heavy" (the <see cref="HeavyMs"/> ceiling).</summary>
    public const float HeavyDamageRef = 30f;

    /// <summary>Hits dealing less than this never freeze — trivial chip stays fluid.</summary>
    public const float MinDamage = 4f;

    public const int LightMs = 45;   // a just-qualifying hit
    public const int HeavyMs = 110;  // a heavy (>= HeavyDamageRef) hit
    public const int CritBonusMs = 40;
    public const int StaggerMs = 160; // a poise-break — strictly the longest stop (> a heavy crit)
    public const int BlockMs = 30;    // a blocked hit: a light "tink", no real weight

    /// <summary>Freeze duration in ms for a hit of <paramref name="amount"/> damage. Blocked hits get a
    /// small fixed tick; otherwise the stop scales from <see cref="LightMs"/> to <see cref="HeavyMs"/>
    /// by damage, plus a crit bonus; a poise-break (<paramref name="staggered"/>) is the longest.
    /// Returns 0 below <see cref="MinDamage"/> (unless staggered) so trivial hits don't freeze.</summary>
    public static int DurationMs(float amount, bool isCrit, bool isBlocked, bool staggered)
    {
        if (staggered)
        {
            return StaggerMs;
        }

        if (isBlocked)
        {
            return BlockMs;
        }

        if (amount < MinDamage)
        {
            return 0;
        }

        float t = amount / HeavyDamageRef;
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        int ms = LightMs + (int)((HeavyMs - LightMs) * t);
        return isCrit ? ms + CritBonusMs : ms;
    }

    /// <summary>A parry: the attacker's own blow is stopped dead by the guard. Held long enough to
    /// read as a clash, shorter than a poise break.</summary>
    public const int ParryMs = 140;

    /// <summary>A guard giving out under a blow: the longest stop of all, because it is the moment
    /// the fight turns against the defender.</summary>
    public const int GuardBreakMs = 190;

    /// <summary>Nothing freezes longer than this, whatever stacks on it.</summary>
    public const int MaxMs = 240;

    /// <summary>Stops shorter than this are a slow-motion drag rather than a hard freeze, because a
    /// 40 ms full stop reads as a dropped frame, not as weight.</summary>
    public const int HardFreezeMs = 70;

    /// <summary>The time scale of a drag (a stop shorter than <see cref="HardFreezeMs"/>).</summary>
    public const float DragTimeScale = 0.12f;

    /// <summary>
    /// The full stop for a resolved blow: how long, and how deep. Weight comes from the outcome first
    /// (a guard break outweighs a parry outweighs a poise break outweighs a hit), then from the damage
    /// and the kind of blow (a heavy or charged swing and a riposte weigh more; an arrow or a bolt
    /// barely register), and an authored <paramref name="actionScale"/>
    /// (<c>ActionDefinitionResource.HitStopScale</c>) multiplies the lot. Returns
    /// <see cref="HitStopPlan.None"/> for a blow too light to freeze on.
    /// </summary>
    public static HitStopPlan Plan(
        HitOutcome outcome, HitKind kind, float amount, bool staggered, float actionScale = 1f)
    {
        float ms;
        switch (outcome)
        {
            case HitOutcome.GuardBroken:
                ms = GuardBreakMs;
                break;
            case HitOutcome.Parried:
                ms = ParryMs;
                break;
            case HitOutcome.PoiseBroken:
                ms = StaggerMs;
                break;
            case HitOutcome.Blocked:
                ms = BlockMs;
                break;
            case HitOutcome.Resisted:
                ms = DurationMs(amount, false, false, false) * 0.5f;
                break;
            default:
                ms = DurationMs(amount, outcome == HitOutcome.Critical, false, false);
                break;
        }

        // A crit or a plain hit that also broke poise holds for the poise-break stop at least.
        if (staggered && outcome is HitOutcome.Hit or HitOutcome.Critical or HitOutcome.Resisted)
        {
            ms = Math.Max(ms, StaggerMs);
        }

        if (outcome is HitOutcome.Hit or HitOutcome.Critical or HitOutcome.PoiseBroken)
        {
            ms *= HitOutcomes.KindWeight(kind);
        }

        ms *= actionScale < 0f ? 0f : actionScale;
        int rounded = (int)Math.Min(Math.Round(ms), MaxMs);
        return rounded <= 0 ? HitStopPlan.None : new HitStopPlan(rounded, rounded < HardFreezeMs ? DragTimeScale : 0f);
    }

    /// <summary>
    /// The plan after the player's comfort scale (<see cref="CombatComfort.HitStop"/>, already capped
    /// by Reduced Motion). The duration shrinks with it and so does the depth: at a quarter scale a
    /// hit dips the clock to 0.25x for a quarter of the time rather than freezing it for a blip. A
    /// scale of 0 is no stop at all.
    /// </summary>
    public static HitStopPlan Scale(in HitStopPlan plan, float comfort)
    {
        if (plan.Milliseconds <= 0 || comfort <= 0f)
        {
            return HitStopPlan.None;
        }

        float c = comfort > 1f ? 1f : comfort;
        int ms = (int)Math.Round(plan.Milliseconds * c);
        if (ms <= 0)
        {
            return HitStopPlan.None;
        }

        return new HitStopPlan(ms, 1f - ((1f - plan.TimeScale) * c));
    }
}

/// <summary>A freeze frame to apply: <see cref="Milliseconds"/> of wall-clock time at
/// <see cref="TimeScale"/> (0 = a hard freeze).</summary>
public readonly record struct HitStopPlan(int Milliseconds, float TimeScale)
{
    /// <summary>No stop.</summary>
    public static readonly HitStopPlan None = new(0, 1f);

    public bool IsNone => Milliseconds <= 0;
}

/// <summary>
/// A budget on freeze time, so a flurry of hits cannot stack into a stall. Any trailing window of
/// <see cref="WindowMs"/> may hold at most <see cref="BudgetMs"/> of granted freeze; once it is spent
/// further stops shrink and then vanish until the window slides on. Pure, clocked by the caller, so a
/// hitch or a pause cannot corrupt it and the director's real clock is not needed to test it.
/// </summary>
public sealed class HitStopLimiter
{
    public const int WindowMs = 700;
    public const int BudgetMs = 300;

    /// <summary>A grant smaller than this is not worth interrupting the clock for.</summary>
    public const int MinGrantMs = 20;

    private readonly System.Collections.Generic.List<(ulong At, int Ms)> _grants = new();

    /// <summary>How many milliseconds of a <paramref name="requestedMs"/> request may run at
    /// <paramref name="nowMs"/>, and records the grant.</summary>
    public int Admit(ulong nowMs, int requestedMs)
    {
        if (requestedMs <= 0)
        {
            return 0;
        }

        _grants.RemoveAll(g => nowMs >= g.At + WindowMs);
        int used = 0;
        foreach ((ulong _, int ms) in _grants)
        {
            used += ms;
        }

        int granted = Math.Min(requestedMs, BudgetMs - used);
        if (granted < MinGrantMs)
        {
            return 0;
        }

        _grants.Add((nowMs, granted));
        return granted;
    }

    /// <summary>Forgets every grant (a load, a scene change).</summary>
    public void Reset() => _grants.Clear();
}
