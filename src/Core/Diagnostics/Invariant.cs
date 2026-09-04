namespace Embervale.Core.Diagnostics;

/// <summary>
/// Lightweight runtime invariant checks. Unlike a hard <c>assert</c> these never throw —
/// a violated invariant is logged (and counted) so the game keeps running and the issue is
/// surfaced in the log / dev console rather than crashing a play session. Use it to assert
/// the assumptions systems rely on (non-negative resources, resolved references, no NaNs).
///
/// <para>It lives in <c>Core.Diagnostics</c> rather than <c>Debugging</c> because Core itself
/// asserts with it — service-scope ownership does — and the layering rule is that Core may not
/// depend on the tooling layer.</para>
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
