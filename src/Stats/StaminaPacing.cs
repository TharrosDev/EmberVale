using System;

namespace Embervale.Stats;

/// <summary>
/// Pure stamina-pacing rules (Phase 29I, the anti-mash lever from DESIGN §1.4/§1.6; regen curve, winded
/// state and Endurance scaling added in the movement pass). Godot-free so it's unit-testable;
/// <see cref="StatsComponent"/> applies it.
///
/// <para>Stamina regen is <b>paused under load</b>: it only ticks once the owner has gone <c>delay</c>
/// seconds without spending, so "attack, attack, attack" drains to empty while "read, punish, recover" lets
/// it refill. Once it resumes it <b>ramps in</b> rather than snapping to full rate, runs <b>slower near
/// empty</b>, slows while the guard is up, and scales with <see cref="StatType.Endurance"/>. Draining it to
/// zero leaves the owner <b>winded</b> until it refills past a threshold — the state
/// <c>DodgeComponent</c> and the sprint request honour.</para>
/// </summary>
public static class StaminaPacing
{
    /// <summary>True once enough idle time has passed since the last spend for stamina to regen again.</summary>
    public static bool CanRegen(double idleElapsed, float delay) => idleElapsed >= delay;

    /// <summary>Regen ramp after the pause: 0 before <paramref name="delay"/>, then rising linearly from
    /// <paramref name="startFraction"/> to 1 over <paramref name="rampSeconds"/>. A snap to full rate made
    /// the bar jump the instant the pause lifted; the ramp makes recovery something you wait into.</summary>
    public static float RampFactor(double idleElapsed, float delay, float rampSeconds, float startFraction)
    {
        if (!CanRegen(idleElapsed, delay))
        {
            return 0f;
        }

        if (rampSeconds <= 0f)
        {
            return 1f;
        }

        float start = Math.Clamp(startFraction, 0f, 1f);
        float t = (float)Math.Min((idleElapsed - delay) / rampSeconds, 1d);
        return start + ((1f - start) * t);
    }

    /// <summary>Low-stamina slowdown: 1 at or above <paramref name="knee"/> (fraction of max), easing down
    /// to <paramref name="floor"/> at empty — the last sliver refills slowest, so running dry costs time.</summary>
    public static float LowStaminaFactor(float fraction, float knee, float floor)
    {
        if (knee <= 0f || fraction >= knee)
        {
            return 1f;
        }

        float f = Math.Clamp(floor, 0f, 1f);
        return f + ((1f - f) * Math.Clamp(fraction / knee, 0f, 1f));
    }

    /// <summary>Attribute scaling: 1 + (attribute − baseline) × perPoint, never below
    /// <paramref name="minimum"/>. At the default baseline every existing actor regenerates exactly as it
    /// did; Endurance gear, perks and progression now buy stamina recovery.</summary>
    public static float AttributeFactor(float attribute, float baseline, float perPoint, float minimum) =>
        Math.Max(minimum, 1f + ((attribute - baseline) * perPoint));

    /// <summary>
    /// The winded latch. Hitting zero sets it; it holds until stamina has refilled to
    /// <paramref name="recoverFraction"/> of max. The hysteresis is the point: without it a drained owner
    /// could dodge again on the first sliver of regen and the empty bar would mean nothing.
    /// </summary>
    public static bool UpdateWinded(bool winded, float fraction, float recoverFraction)
    {
        if (fraction <= 0f)
        {
            return true;
        }

        return winded && fraction < recoverFraction;
    }
}
