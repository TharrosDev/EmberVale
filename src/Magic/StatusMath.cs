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
}
