using Embervale.Core.Diagnostics;

namespace Embervale.Debugging;

/// <summary>
/// Lightweight runtime invariant checks. Unlike a hard <c>assert</c> these never throw —
/// a violated invariant is logged (and counted) so the game keeps running and the issue is
/// surfaced in the log / dev console rather than crashing a play session. Use it to assert
/// the assumptions systems rely on (non-negative resources, resolved references, no NaNs).
/// </summary>
public static class Invariant
{
    /// <summary>Total invariant violations recorded this session.</summary>
    public static int Violations { get; private set; }

    /// <summary>Returns <paramref name="condition"/>; logs + counts a violation when it is false.</summary>
    public static bool Check(bool condition, string message)
    {
        if (!condition)
        {
            Violations++;
            Log.Error($"[invariant] {message}");
        }

        return condition;
    }

    public static void Reset() => Violations = 0;
}
