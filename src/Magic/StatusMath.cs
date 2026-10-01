using System;
using System.Collections.Generic;

namespace Embervale.Magic;

/// <summary>
/// Pure damage-over-time cadence behind <see cref="StatusEffectsComponent"/>. Kept Godot-free so the
/// catch-up tick logic — how many DoT ticks a frame of <c>delta</c> fires — is unit-testable apart
/// from the stats it damages.
/// </summary>
public static class StatusMath
{
    /// <summary>
    /// Advances a DoT's tick timer by <paramref name="delta"/> and reports how many ticks fire. A tick
    /// is due each time the timer reaches <c>&lt;= 0</c>, after which <paramref name="interval"/> is
    /// added back — so a large <paramref name="delta"/> that spans several intervals catches up all of
    /// them. Returns the new timer (the carry-over toward the next tick). A non-positive
    /// <paramref name="interval"/> is a no-op (0 ticks, timer unchanged) so it can never loop forever.
    /// </summary>
    public static (int Ticks, double NewTimer) AdvanceDot(double tickTimer, double delta, double interval)
    {
        if (interval <= 0d)
        {
            return (0, tickTimer);
        }

        double timer = tickTimer - delta;
        int ticks = 0;
        while (timer <= 0d)
        {
            ticks++;
            timer += interval;
        }

        return (ticks, timer);
    }

    /// <summary>The stack count after one more application: <paramref name="current"/> + 1, capped at
    /// <paramref name="max"/> (and never below 1). Drives Fire's stacking ignite (Phase 29.5B).</summary>
    public static int NextStack(int current, int max)
    {
        int cap = max < 1 ? 1 : max;
        int next = current + 1;
        return next > cap ? cap : next;
    }

    /// <summary>
    /// Which beneficial effect an Arcane hit strips (Phase 34E.5): the one with the most time left,
    /// or <c>null</c> when the target has no buff to lose. Harmful effects are never eligible —
    /// stripping a target's own burning would be a gift, not a dispel.
    ///
    /// Longest-remaining rather than first-found because the caller's source is a dictionary's
    /// values, whose order is not a contract; "the first one" would be silently arbitrary. Ties
    /// break on the ordinal id so the same fight resolves the same way twice (the determinism
    /// <c>ReproHarness</c> depends on).
    ///
    /// Takes plain tuples so it stays Godot-free: a <see cref="StatusEffect"/> carries a
    /// <see cref="StatusEffectResource"/>, and neither can be built in the unit-test project.
    /// </summary>
    public static string? PickDispel(IEnumerable<(string Id, bool IsBeneficial, double Remaining)> effects)
    {
        string? best = null;
        double bestRemaining = double.NegativeInfinity;

        foreach ((string id, bool beneficial, double remaining) in effects)
        {
            if (!beneficial)
            {
                continue;
            }

            if (remaining > bestRemaining ||
                (remaining == bestRemaining && best != null && string.CompareOrdinal(id, best) < 0))
            {
                best = id;
                bestRemaining = remaining;
            }
        }

        return best;
    }

    // --- magic upgrade 2026-09: the rules behind StatusEffectsComponent, kept over primitives ---

    /// <summary>The controls that lock an actor down and so fall under diminishing returns. A mark is
    /// information, not a lock, and is never immune-able.</summary>
    public const StatusControl HardControls = StatusControl.Root | StatusControl.Silence | StatusControl.Stun;

    /// <summary>Just the lock-down bits of a control set.</summary>
    public static StatusControl HardOf(StatusControl controls) => controls & HardControls;

    /// <summary>True when a fresh status carrying <paramref name="incoming"/> controls must be refused
    /// because the bearer is still immune to at least one of the lock-downs it would add.</summary>
    public static bool IsControlRefused(StatusControl incoming, StatusControl immune) =>
        (HardOf(incoming) & immune) != StatusControl.None;

    /// <summary>Stack count at which a detonating status goes off (0 = never detonates).</summary>
    public static bool ShouldDetonate(int stacks, int detonateAt) => detonateAt > 0 && stacks >= detonateAt;

    /// <summary>Damage of a detonation: per-stack damage times the stacks it consumed.</summary>
    public static float DetonateDamage(int stacks, float perStack) =>
        Math.Max(0, stacks) * Math.Max(0f, perStack);

    /// <summary>Incoming damage after a mark: <paramref name="amplify"/> is the summed positive
    /// <c>DamageTakenModifier</c>, capped at +100% so stacked marks cannot run away.</summary>
    public static float Amplify(float amount, float amplify) =>
        Math.Max(0f, amount) * (1f + Math.Clamp(amplify, 0f, 1f));

    /// <summary>Incoming damage after a flat fractional reduction (a negative modifier with no pool),
    /// capped at 90% so nothing is ever immune.</summary>
    public static float Reduce(float amount, float reduction) =>
        Math.Max(0f, amount) * (1f - Math.Clamp(reduction, 0f, 0.9f));

    /// <summary>A ward eating a hit. <paramref name="fraction"/> (0..1) of the hit goes into the pool
    /// until the pool is spent; the rest passes through. Returns what still lands and the pool left.</summary>
    public static (float Passed, float PoolLeft) Absorb(float amount, float fraction, float pool)
    {
        float hit = Math.Max(0f, amount);
        float wanted = hit * Math.Clamp(fraction, 0f, 1f);
        float used = Math.Min(wanted, Math.Max(0f, pool));
        return (hit - used, Math.Max(0f, pool) - used);
    }

    /// <summary>Ward capacity: authored base plus a share of the caster's spell power.</summary>
    public static float WardCapacity(float baseAmount, float spellPower, float perSpellPower) =>
        Math.Max(0f, baseAmount) + (Math.Max(0f, spellPower) * Math.Max(0f, perSpellPower));

    /// <summary>Mana an expiring ward returns to its caster: the unspent share of its authored maximum.
    /// A broken or dispelled ward returns nothing (the caller simply does not ask).</summary>
    public static float WardManaReturn(float maxReturn, float poolLeft, float capacity) =>
        capacity <= 0f ? 0f : Math.Max(0f, maxReturn) * Math.Clamp(poolLeft / capacity, 0f, 1f);

    /// <summary>Multiplier for effects that scale per stack consumed or cleansed (Knit Bone's heal,
    /// a spell's <c>BonusPerConsumedStack</c>): 1 + stacks * bonus.</summary>
    public static float StackBonusMultiplier(int stacks, float bonusPerStack) =>
        1f + (Math.Max(0, stacks) * Math.Max(0f, bonusPerStack));

    /// <summary>Whether a spread status may jump again: bounded, so a swarm cannot chain across a
    /// whole cave forever.</summary>
    public static bool CanSpread(int generation, int maxJumps) => generation < maxJumps;

    /// <summary>The nearest spread target, preferring one that does not already carry the status.
    /// Returns an index into <paramref name="candidates"/>, or -1 when it is empty.</summary>
    public static int PickSpreadTarget(IReadOnlyList<(float DistanceSquared, bool AlreadyHas)> candidates)
    {
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (best < 0 || Better(candidates[i], candidates[best]))
            {
                best = i;
            }
        }

        return best;

        static bool Better((float D, bool Has) a, (float D, bool Has) b) =>
            a.Has != b.Has ? !a.Has : a.D < b.D;
    }
}
