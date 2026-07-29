namespace Embervale.Enemies;

/// <summary>
/// The pure guard brain for a shielded enemy (Phase 34A): a shield-carrier alternates between
/// holding its guard up and dropping it to swing, so the player gets a readable rhythm to punish
/// rather than a coin-flip. Deterministic on purpose — a random block is unlearnable, and an
/// unlearnable defence reads as unfair rather than hard.
///
/// Godot-free so the rhythm is unit-testable apart from the combat component it drives.
/// </summary>
public static class GuardCycle
{
    /// <summary>
    /// Whether the guard is up <paramref name="elapsed"/> seconds into a fight, given a profile that
    /// blocks for <paramref name="upSeconds"/> then opens for <paramref name="downSeconds"/>.
    /// A profile with no block duration never raises its guard.
    /// </summary>
    public static bool IsUp(double elapsed, float upSeconds, float downSeconds)
    {
        if (upSeconds <= 0f)
        {
            return false;
        }

        float period = upSeconds + downSeconds;
        if (period <= 0f)
        {
            return true;   // blocks with no recovery window: a permanent guard
        }

        double phase = elapsed % period;
        if (phase < 0d)
        {
            phase += period;   // negative elapsed shouldn't invert the rhythm
        }

        return phase < upSeconds;
    }
}
