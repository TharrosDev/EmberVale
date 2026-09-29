using System;
using Godot;

namespace Embervale.Combat;

/// <summary>How hard a body is thrown by a blow: the shove along the blow, the lean of the body
/// back from it, and how long it takes to settle.</summary>
public readonly record struct Recoil(float Distance, float LeanRadians, float SettleSeconds);

/// <summary>
/// Pure maths for the weight of a hit reaction (the visual lurch): how far and how long a body is
/// thrown, and the damped spring that settles it. Godot-free apart from <see cref="Vector3"/>, so the
/// feel is unit-tested. A light hit is a nudge; a guard break or a poise break rocks the whole body
/// back and takes visibly longer to recover, which is what makes a heavy blow read as heavy.
/// </summary>
public static class HitReactionMath
{
    /// <summary>The largest lean, radians (about 12.6 degrees).</summary>
    public const float MaxLean = 0.22f;

    public const float MinSettleSeconds = 0.16f;
    public const float MaxSettleSeconds = 0.5f;

    /// <summary>Damping ratio of the settle spring: under 1, so a heavy hit overshoots once and
    /// rocks back, the way a body with weight does. A ratio of 1 would just ease.</summary>
    public const float DampingRatio = 0.6f;

    /// <summary>The largest single displacement, as a multiple of the authored base distance.</summary>
    public const float MaxDistanceScale = 2.6f;

    /// <summary>How much an outcome throws a body (1 = an ordinary hit).</summary>
    public static float OutcomeWeight(HitOutcome outcome) => outcome switch
    {
        HitOutcome.Blocked => 0.35f,
        HitOutcome.Parried => 1.1f,
        HitOutcome.GuardBroken => 1.7f,
        HitOutcome.Critical => 1.4f,
        HitOutcome.PoiseBroken => 1.8f,
        HitOutcome.Resisted => 0.5f,
        _ => 1f,
    };

    /// <summary>
    /// The recoil of a body struck by a blow. <paramref name="baseDistance"/> is the authored
    /// <c>RecoilDistance</c>. Damage sets the floor (a scratch still nudges) up to the heavy
    /// reference; the outcome and the kind of blow scale it; a poise break is never less than the
    /// heaviest outcome.
    /// </summary>
    public static Recoil For(HitOutcome outcome, HitKind kind, float amount, bool staggered, float baseDistance)
    {
        float damage = Math.Clamp(amount / HitStop.HeavyDamageRef, 0.25f, 1f);
        // A bolt or an arrow still knocks a body; the kind weight only matters for the heavy end.
        float kindWeight = Math.Max(HitOutcomes.KindWeight(kind), 0.75f);
        float weight = OutcomeWeight(outcome) * damage * kindWeight;
        if (staggered)
        {
            weight = Math.Max(weight, OutcomeWeight(HitOutcome.PoiseBroken));
        }

        float distance = Math.Min(Math.Max(baseDistance, 0f) * weight, Math.Max(baseDistance, 0f) * MaxDistanceScale);
        float heaviness = Math.Clamp(weight / OutcomeWeight(HitOutcome.PoiseBroken), 0f, 1f);
        float lean = MaxLean * heaviness;
        float settle = MinSettleSeconds + ((MaxSettleSeconds - MinSettleSeconds) * heaviness);
        return new Recoil(distance, lean, settle);
    }

    /// <summary>Angular frequency of the settle spring for a settle time (to about 2%).</summary>
    public static float Omega(float settleSeconds) =>
        4f / (DampingRatio * Math.Max(settleSeconds, 0.05f));

    /// <summary>Advances a damped spring holding a value at <paramref name="x"/> with velocity
    /// <paramref name="v"/> toward 0. Sub-stepped, so a long frame cannot blow it up.</summary>
    public static (float X, float V) Step(float x, float v, float dt, float settleSeconds)
    {
        float omega = Omega(settleSeconds);
        float remaining = Math.Clamp(dt, 0f, 0.1f);
        while (remaining > 0f)
        {
            float h = Math.Min(remaining, 1f / 120f);
            v += ((-omega * omega * x) - (2f * DampingRatio * omega * v)) * h;
            x += v * h;
            remaining -= h;
        }

        return (x, v);
    }

    /// <summary>The same spring on a vector.</summary>
    public static (Vector3 X, Vector3 V) Step(Vector3 x, Vector3 v, float dt, float settleSeconds)
    {
        float omega = Omega(settleSeconds);
        float remaining = Math.Clamp(dt, 0f, 0.1f);
        while (remaining > 0f)
        {
            float h = Math.Min(remaining, 1f / 120f);
            v += ((x * (-omega * omega)) - (v * (2f * DampingRatio * omega))) * h;
            x += v * h;
            remaining -= h;
        }

        return (x, v);
    }

    /// <summary>The axis (world space) that leans a body's head along <paramref name="away"/>, the
    /// horizontal direction the blow pushes it. Rotating by a positive angle about it tips the top
    /// of the body along that direction.</summary>
    public static Vector3 LeanAxis(Vector3 away)
    {
        Vector3 flat = new(away.X, 0f, away.Z);
        return flat.LengthSquared() < 1e-6f ? Vector3.Zero : Vector3.Up.Cross(flat.Normalized());
    }
}
