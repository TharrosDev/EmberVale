using System.Collections.Generic;

namespace Embervale.Shrines;

/// <summary>Godot-free claimed-shrine rules, kept here so first-visit and wholesale-load behaviour
/// are pinned by the ordinary unit test suite.</summary>
public static class BlessingRules
{
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
