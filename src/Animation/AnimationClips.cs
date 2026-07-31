using System;
using System.Collections.Generic;

namespace Embervale.Animation;

/// <summary>
/// Matches a gameplay animation slot (idle, run, attack…) to whatever a model's clips are actually
/// called. Pure string work, deliberately free of Godot so it can be unit-tested.
///
/// <b>Why this exists (Phase 35 asset migration).</b> The in-house 30B/30C models were authored to
/// this project's own vocabulary — <c>idle-loop</c>, <c>run-loop</c>, <c>attack</c> — so a bare
/// "does the name start with the slot" test was enough. Downloaded rigs are not authored to it, and
/// they miss in two independent ways:
///
/// <list type="number">
/// <item>An <b>armature prefix</b>: Quaternius, Mixamo and KayKit all export
/// <c>CharacterArmature|Idle</c>, which does not <i>start with</i> "idle" and so silently resolved
/// to nothing.</item>
/// <item>A <b>different word</b> for the same beat: <c>Walk</c> for run, <c>Bite_Front</c> for
/// attack, <c>HitRecieve</c> (sic — Quaternius ships the typo) for hit.</item>
/// </list>
///
/// Both failures are silent: <see cref="Resolve"/> returning empty leaves the actor in its bind
/// pose, which reads as a T-posing enemy rather than an error anyone can grep for. Fixing it here
/// rather than renaming clips per model means every future pack works on import.
/// </summary>
public static class AnimationClips
{
    /// <summary>Accepted spellings per slot, best match first. A slot always lists its own name
    /// first so this project's own models keep winning outright.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new[] { "idle" },
        ["run"] = new[] { "run", "walk" },
        ["block"] = new[] { "block", "guard", "shield" },
        ["attack"] = new[] { "attack", "bite", "slash", "punch", "swing", "melee" },
        ["hit"] = new[] { "hit", "damage", "impact", "recieve", "receive" },
        ["death"] = new[] { "death", "die", "dead" },
        ["cast"] = new[] { "cast", "spell" },
        ["channel"] = new[] { "channel" },
    };

    /// <summary>
    /// The clip name to play for <paramref name="slot"/>, or empty when the model has nothing for it.
    /// Empty is a valid answer — a creature with no block clip simply never plays one — so callers
    /// guard on length rather than treating it as a failure.
    /// </summary>
    public static string Resolve(IEnumerable<string> clipNames, string slot)
    {
        string[] accepted = Aliases.TryGetValue(slot, out string[]? a) ? a : new[] { slot };

        // Alias order is the outer loop so a model with both "attack" and "bite" prefers "attack",
        // regardless of the order the clips happen to be listed in.
        foreach (string candidate in accepted)
        {
            // Exact before prefix. A rig that ships Idle *and* Idle_Gun must resolve to Idle, and
            // relying on the list order to deliver that is luck: the Iron King's replacement happens
            // to list them alphabetically, so a first-match-wins scan is correct there purely by
            // accident. An exact-match pass makes it correct on purpose — the failure it prevents is
            // a fantasy boss idling in a rifle stance, which nothing would flag as an error.
            foreach (string name in clipNames)
            {
                if (string.Equals(Bare(name), candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            // Prefix fallback — this is what matches the in-house "idle-loop"/"run-loop" naming.
            foreach (string name in clipNames)
            {
                if (Bare(name).StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Strips an exporter's armature prefix — <c>CharacterArmature|Idle</c> becomes
    /// <c>Idle</c>. Godot keeps the full name as the clip's key, so only the comparison is stripped;
    /// the value returned to the caller is always the real, playable name.</summary>
    private static string Bare(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 && bar < clipName.Length - 1 ? clipName[(bar + 1)..] : clipName;
    }
}
