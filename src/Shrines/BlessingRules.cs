using System.Collections.Generic;

namespace Embervale.Shrines;

/// <summary>What a shrine visit resolves to. Ordered by how the rule reads, not by severity.</summary>
public enum ShrineOutcome
{
    /// <summary>First visit, corruption below the god's tolerance: the blessing is claimed.</summary>
    Blessed,

    /// <summary>The god refuses a tainted supplicant. Nothing is claimed and no modifier applies.</summary>
    Refused,

    /// <summary>The blessing was already claimed; a shrine grants once and is never revoked.</summary>
    AlreadyClaimed,
}

/// <summary>Godot-free claimed-shrine rules, kept here so first-visit and wholesale-load behaviour
/// are pinned by the ordinary unit test suite.</summary>
public static class BlessingRules
{
    /// <summary>The whole visit rule, in one place: <b>already-claimed → refused → blessed</b>.
    ///
    /// ⚠️ The order is the design. A player who claimed at low corruption and later fell keeps the
    /// blessing — corruption gates the *granting*, never revokes a granted passive — so the
    /// already-claimed test has to come first or a fallen player would read a refusal for a
    /// blessing they still carry. And refusal is checked <b>before</b> anything is added, so the
    /// refused branch cannot leave a claimed id behind (41.5B trap 1).</summary>
    public static ShrineOutcome Decide(ISet<string> claims, string shrineId, int corruption, int refusalAt)
    {
        if (string.IsNullOrEmpty(shrineId) || claims.Contains(shrineId))
        {
            return ShrineOutcome.AlreadyClaimed;
        }

        return corruption >= refusalAt ? ShrineOutcome.Refused : ShrineOutcome.Blessed;
    }

    /// <summary>Adds a shrine id exactly once. Empty ids never become save state.</summary>
    public static bool TryClaim(ISet<string> claims, string shrineId)
    {
        return !string.IsNullOrEmpty(shrineId) && claims.Add(shrineId);
    }

    /// <summary>Replaces the claimed set, never merging an earlier run over a loaded one.</summary>
    public static void ReplaceClaims(ISet<string> claims, IEnumerable<string> restoredIds)
    {
        claims.Clear();
        foreach (string id in restoredIds)
        {
            if (!string.IsNullOrEmpty(id))
            {
                claims.Add(id);
            }
        }
    }
}
