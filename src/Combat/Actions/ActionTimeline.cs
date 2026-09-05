namespace Embervale.Combat.Actions;

/// <summary>Where a running action is along its own timeline. Runtime-only — never authored into a
/// .tres and never saved, so it is deliberately not pinned by EnumStabilityTests.</summary>
public enum ActionPhase
{
    /// <summary>Nothing is running.</summary>
    Idle,

    /// <summary>Committed and winding up; the hit window has not opened.</summary>
    Startup,

    /// <summary>The hit window is open.</summary>
    Active,

    /// <summary>The blow is spent; the actor is recovering and may become cancellable.</summary>
    Recovery,
}

/// <summary>
/// The gameplay windows of one action, expressed as <b>fractions of the action's own duration</b>
/// rather than as seconds.
///
/// ⚠️ THIS IS THE WHOLE POINT OF THE REBUILD. The system this replaced ran a <c>double</c> stopwatch
/// in <c>MeleeWeaponComponent</c> while <c>CharacterAnimationComponent</c> fired a one-shot
/// clip at whatever speed it happened to be authored at — so the Iron King's 0.55 s wind-up and a
/// dagger's 0.15 s played the same <c>Sword_Slash</c> identically, and the hitbox opened on a clock
/// the visible swing had never heard of. Fractions cannot desynchronise from the clip, because the
/// clip is fitted to the same duration the fractions are measured against.
/// </summary>
public readonly record struct ActionWindows(
    float ActiveFrom,
    float ActiveTo,
    float CancelFrom,
    float ComboFrom,
    float ComboTo)
{
    /// <summary>A sane melee shape for an action that authored nothing: a third wind-up, a short
    /// live window, the rest recovery, cancellable and combo-able once the blow is spent.</summary>
    public static readonly ActionWindows Default = new(0.34f, 0.52f, 0.62f, 0.52f, 1f);
}

/// <summary>
/// Pure timeline arithmetic — no Godot, no state. <see cref="CharacterActionComponent"/> supplies
/// the progress and applies the answers; every rule below is unit-tested in
/// <c>ActionTimelineTests</c>.
///
/// Progress is <c>0..1</c> across the whole action. Whether that progress came from an
/// <c>AnimationPlayer</c>'s playback position or from a fallback timer is deliberately not this
/// type's business: both are the same number, which is what makes the fallback safe.
/// </summary>
public static class ActionTimeline
{
    /// <summary>Which phase <paramref name="progress"/> falls in.</summary>
    public static ActionPhase PhaseAt(float progress, ActionWindows w)
    {
        if (progress >= 1f)
        {
            return ActionPhase.Idle;
        }

        if (progress < w.ActiveFrom)
        {
            return ActionPhase.Startup;
        }

        return progress < w.ActiveTo ? ActionPhase.Active : ActionPhase.Recovery;
    }

    /// <summary>True while the hit window is open. The hitbox is opened on the rising edge of this
    /// and closed on the falling edge — never on a separate timer.</summary>
    public static bool IsActive(float progress, ActionWindows w) =>
        progress >= w.ActiveFrom && progress < w.ActiveTo && progress < 1f;

    /// <summary>
    /// True once the action may be cancelled into something else.
    ///
    /// ⚠️ Commitment is the inverse of this, and it is what stops attack spam. Before
    /// <see cref="ActionWindows.CancelFrom"/> the actor is committed: no new action, no dodge, and
    /// a press is buffered rather than dropped.
    /// </summary>
    public static bool CanCancel(float progress, ActionWindows w) => progress >= w.CancelFrom;

    /// <summary>True while a press would chain into the next link of a combo rather than restart it.</summary>
    public static bool InComboWindow(float progress, ActionWindows w) =>
        progress >= w.ComboFrom && progress <= w.ComboTo;

    /// <summary>
    /// True when a stagger landing at <paramref name="progress"/> should cancel the action outright.
    ///
    /// Only the startup is interruptible: once the blow is live it is committed, which is what keeps
    /// the punish window something to aim for rather than a race. Carried over verbatim from the
    /// system this replaces (36C) — staggering a boss mid-wind-up must actually stop the blow.
    /// </summary>
    public static bool StaggerCancels(float progress, ActionWindows w, bool interruptible) =>
        interruptible && progress < w.ActiveFrom;

    /// <summary>
    /// The animation <c>speed_scale</c> that makes a clip of <paramref name="clipSeconds"/> span
    /// exactly <paramref name="actionSeconds"/>. This is the other half of the contract: the
    /// fractions above describe the action, and this makes the clip agree with them.
    /// </summary>
    public static float ClipSpeedFor(float clipSeconds, float actionSeconds)
    {
        if (clipSeconds <= 0f || actionSeconds <= 0f)
        {
            return 1f;
        }

        // Clamped because a pathological pairing (a 4 s clip forced into a 0.05 s action) would
        // otherwise produce a speed that reads as a single flickering frame rather than a swing.
        float speed = clipSeconds / actionSeconds;
        return speed < 0.1f ? 0.1f : speed > 12f ? 12f : speed;
    }

    /// <summary>Progress from a plain elapsed/duration pair, clamped. The fallback used when the
    /// actor has no clip for the slot — a body with no animation still fights correctly.</summary>
    public static float ProgressOf(double elapsed, double duration)
    {
        if (duration <= 0d)
        {
            return 1f;
        }

        double p = elapsed / duration;
        return p <= 0d ? 0f : p >= 1d ? 1f : (float)p;
    }
}
