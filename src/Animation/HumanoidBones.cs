using System;
using Godot;

namespace Embervale.Animation;

/// <summary>
/// Finds the bones gameplay hangs things off — currently just the hands, for the weapon socket and
/// the spell flash.
///
/// <b>Why this exists (Phase 38A).</b> Two call sites resolved the hand by the same heuristic:
/// a bone whose name contains "hand" and ends in "R". **Every adopted Quaternius body calls it
/// <c>Wrist.R</c>**, so both had been silently returning nothing — the player's visual sword was
/// being <c>QueueFree</c>d on every spawn and the cast flash was falling back to chest height. A
/// heuristic that misses is invisible: nothing logs, and a sword that is not there looks exactly
/// like a build that never had one.
///
/// Retargeting (38A) gives the honest fix: a rig mapped onto <c>SkeletonProfileHumanoid</c> names
/// the bone <c>RightHand</c>, which is knowable rather than guessable. The heuristic stays as the
/// fallback for the rigs that are not retargeted yet, widened to the names those rigs actually use.
/// </summary>
public static class HumanoidBones
{
    /// <summary>The hand bone's name, or empty when the rig has nothing recognisable. Empty is a
    /// valid answer — a quadruped has no hand — so callers guard on length.</summary>
    public static string FindHand(Skeleton3D skeleton, bool right)
    {
        // 1. The profile name, which is exact and correct on any retargeted rig.
        string profile = right ? "RightHand" : "LeftHand";
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            if (string.Equals(skeleton.GetBoneName(i), profile, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        // 2. Otherwise the pack's own word for it, with the side as a suffix. "wrist" is listed
        // because that is what Quaternius calls the hand, and its absence here is the whole bug.
        string suffix = right ? "R" : "L";
        foreach (string word in new[] { "hand", "wrist" })
        {
            for (int i = 0; i < skeleton.GetBoneCount(); i++)
            {
                string name = skeleton.GetBoneName(i);
                if (name.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return string.Empty;
    }
}
