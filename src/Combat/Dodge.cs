using System;

namespace Embervale.Combat;

/// <summary>
/// Pure dodge rules (Phase 29E; the directional/curve/recovery upgrade of the movement pass). Godot-free
/// so every timing, gate and cost is unit-testable; <see cref="DodgeComponent"/> drives the roll from
/// these and owns none of the arithmetic itself.
///
/// <para>A dodge is two phases on one clock: <b>motion</b> [0, duration) — an ease-out burst with the
/// i-frame window inside it — then a short <b>recovery</b> tail where the body is back under input but
/// no new dodge may start. Presses in either phase are buffered, not dropped. An attack may cancel the
/// roll from <see cref="CanCancelIntoAttack"/> onward, which is placed after the i-frames close so a
/// roll-attack never carries invulnerability into the swing.</para>
/// </summary>
public static class Dodge
{
    /// <summary>True while the roll's invulnerability window is open: <paramref name="elapsed"/> in
    /// [<paramref name="iframeStart"/>, <paramref name="iframeEnd"/>).</summary>
    public static bool IsInvulnerable(float elapsed, float iframeStart, float iframeEnd) =>
        elapsed >= iframeStart && elapsed < iframeEnd;

    /// <summary>Whether a dodge may start: grounded, enough stamina, not already dodging (motion or
    /// recovery), not staggered, and not <paramref name="winded"/> (stamina ran dry and has not yet
    /// refilled past the recovery threshold — see <c>StaminaPacing.UpdateWinded</c>).</summary>
    public static bool CanStart(bool grounded, float stamina, float cost, bool rolling, bool staggered,
        bool winded = false) =>
        grounded && stamina >= cost && !rolling && !staggered && !winded;

    /// <summary>Which dodge a press asks for: a roll toward the held direction, or a backstep when the
    /// stick/keys are inside <paramref name="deadzone"/> (squared length compared against its square).
    /// Skyrim-plus: a neutral press is a short hop back out of reach, not a roll into the enemy's face.</summary>
    public static DodgeKind Resolve(float inputLengthSquared, float deadzone) =>
        inputLengthSquared > deadzone * deadzone ? DodgeKind.Roll : DodgeKind.Backstep;

    /// <summary>
    /// Ease-out burst speed at <paramref name="elapsed"/> into a motion phase of
    /// <paramref name="duration"/>: <paramref name="peakSpeed"/> at the push-off, easing to
    /// <paramref name="endFraction"/> × peak at the end along <c>(1 − t)^exponent</c>. The flat burst it
    /// replaces started and stopped at one velocity, which read as a teleporting slide; this reads as a
    /// push and a settle. 0 outside the phase.
    /// </summary>
    public static float SpeedAt(float elapsed, float duration, float peakSpeed, float endFraction, float exponent)
    {
        if (duration <= 0f || elapsed < 0f || elapsed >= duration)
        {
            return 0f;
        }

        float t = elapsed / duration;
        float end = Math.Clamp(endFraction, 0f, 1f);
        float ease = MathF.Pow(1f - t, Math.Max(exponent, 0.01f));
        return peakSpeed * (end + ((1f - end) * ease));
    }

    /// <summary>Distance a <see cref="SpeedAt"/> burst covers: peak × duration × the curve's mean,
    /// <c>end + (1 − end) / (exponent + 1)</c>. Tuning aid and the test's check on the curve.</summary>
    public static float Distance(float duration, float peakSpeed, float endFraction, float exponent)
    {
        float end = Math.Clamp(endFraction, 0f, 1f);
        return peakSpeed * duration * (end + ((1f - end) / (Math.Max(exponent, 0.01f) + 1f)));
    }

    /// <summary>True once <paramref name="elapsed"/> has passed the motion phase and its
    /// <paramref name="recovery"/> tail — the dodge is over and a new one may start.</summary>
    public static bool IsFinished(float elapsed, float duration, float recovery) =>
        elapsed >= duration + Math.Max(recovery, 0f);

    /// <summary>Whether an attack press may cancel the dodge now: from
    /// <paramref name="cancelFraction"/> of the motion phase onward (through the recovery tail).</summary>
    public static bool CanCancelIntoAttack(float elapsed, float duration, float cancelFraction) =>
        elapsed >= duration * Math.Clamp(cancelFraction, 0f, 1f);

    /// <summary>How many back-to-back dodges this one continues: the previous count + 1 when it starts
    /// within <paramref name="window"/> seconds of the last dodge finishing, else 0.</summary>
    public static int NextChain(int previousChain, double sinceLastEnd, float window) =>
        sinceLastEnd <= window ? previousChain + 1 : 0;

    /// <summary>Stamina cost of a dodge that is the <paramref name="chain"/>-th in a back-to-back run:
    /// <paramref name="baseCost"/> × (1 + surcharge × chain), capped at <paramref name="maxMultiplier"/>.
    /// The anti-spam lever — a single evade stays cheap, a roll-roll-roll escape gets expensive fast —
    /// chosen over a hard cooldown because a cooldown refuses a press the player can see they paid for.</summary>
    public static float ChainedCost(float baseCost, int chain, float surcharge, float maxMultiplier)
    {
        float multiplier = 1f + (Math.Max(surcharge, 0f) * Math.Max(chain, 0));
        return baseCost * Math.Min(multiplier, Math.Max(maxMultiplier, 1f));
    }
}
