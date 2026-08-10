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
        // "flying_idle" is listed for idle, and "fast_flying" for run, because the Quaternius
        // fliers (dragon, demon) ship ONLY those two locomotion clips — no "idle", no "walk". Both
        // are exact matches, so a grounded rig that happens to own a real "idle" still wins on the
        // first alias. Bare "flying" is deliberately NOT a run alias: it would prefix-match
        // "Flying_Idle" and fly the creature on the spot while it stands still.
        ["idle"] = new[] { "idle", "flying_idle" },
        // "jog" sits between run and walk for the 38A library, whose forward locomotion is
        // Jog_Fwd and Walk — a jog reads as this game's run, a walk does not.
        ["run"] = new[] { "run", "jog", "walk", "gallop", "sprint", "fast_flying" },
        // "sword_idle" is the library's only guard pose; it is listed here and NOT reachable from
        // the attack slot, see the note on that slot below.
        ["block"] = new[] { "block", "guard", "shield", "sword_idle" },
        // Weapon-specific words are tried BEFORE the generic "attack". A rig that ships both
        // "Attacking_Idle" (a stance) and "Dagger_Attack" (the swing) would otherwise resolve the
        // attack slot to the stance purely on alphabetical luck — the character would wind up and
        // never strike. A clip named after a weapon is always the swing; "attack" alone is not.
        // ⚠️ 38A: the bare "sword" prefix matches the library's Sword_Attack AND its Sword_Idle, and
        // a retargeted character carries both alongside its own Sword_Slash. Whichever the engine
        // happened to list first would win — that is the same "winds up and never strikes" failure
        // the exact-match pass below was written for, and the exact pass does NOT save it because
        // neither clip is called "sword". The two full names are therefore listed ahead of it:
        // a body's own Sword_Slash first, the library's Sword_Attack second, bare "sword" last.
        ["attack"] = new[]
        {
            "slash", "sword_slash", "sword_attack", "sword", "dagger", "bite", "swing", "attack",
            "punch", "melee",
        },
        ["hit"] = new[] { "hit", "damage", "impact", "recieve", "receive" },
        ["death"] = new[] { "death", "die", "dead" },
        // Spell_Simple_Shoot is the release; Spell_Simple_Enter/Exit are the transitions into and
        // out of the sustained Spell_Simple_Idle. Bare "spell" would take Enter (it sorts first)
        // and the caster would wind up into a pose it never fires from.
        ["cast"] = new[] { "cast", "spell_simple_shoot", "spell" },
        ["channel"] = new[] { "channel", "spell_simple_idle" },
        // 39A, the rider's seat. The library has no riding clip and never will — what it has are two
        // seated poses, and they are not interchangeable: "Driving" holds the hands out in front,
        // which reads as reins, while "Sitting_Idle" rests them in the lap and reads as a passenger.
        // Rendered side by side on the horse before the order was written down. Bare "sitting" is
        // last because it also prefix-matches Sitting_Talking.
        ["ride"] = new[] { "ride", "driving", "sitting_idle", "sitting" },
        // The mount's own second gear. It is deliberately NOT the "run" slot: run lists "walk" ahead
        // of "gallop" (a jog is this game's run), so asking run for a gallop returns the walk and the
        // horse ambles while the player sprints.
        ["gallop"] = new[] { "gallop", "run", "sprint" },
    };

    /// <summary>
    /// The clip name to play for <paramref name="slot"/>, or empty when the model has nothing for it.
    /// Empty is a valid answer — a creature with no block clip simply never plays one — so callers
    /// guard on length rather than treating it as a failure.
    /// </summary>
    public static string Resolve(IEnumerable<string> clipNames, string slot)
    {
        string[] all = clipNames as string[] ?? System.Linq.Enumerable.ToArray(clipNames);

        // A model's OWN clips beat a borrowed one at equal strength (38A). The shared library's
        // importer strips the "_Loop" suffix, so it contributes a clip literally called "Idle" —
        // bare-identical to the body's own "CharacterArmature|Idle", and the exact-match pass below
        // cannot tell them apart. Whichever the engine happened to list first would win, which is
        // alphabetical luck that turns on what the library is named. A body's clips are authored for
        // its own rig and its own proportions; the library is the fallback that fills the gaps. So
        // the un-prefixed set is offered first, and the library only answers what it alone can.
        string[] own = System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Where(all, n => n.IndexOf('/') < 0));

        if (own.Length < all.Length && Match(own, slot) is { Length: > 0 } mine)
        {
            return mine;
        }

        return Match(all, slot);
    }

    private static string Match(string[] clipNames, string slot)
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
    /// <c>Idle</c> — and an <see cref="Godot.AnimationLibrary"/>'s name prefix, so 38A's shared
    /// library reads as <c>Sword_Attack</c> rather than <c>lib/Sword_Attack</c>. Godot keeps the
    /// full name as the clip's key, so only the comparison is stripped; the value returned to the
    /// caller is always the real, playable name.</summary>
    private static string Bare(string clipName)
    {
        int cut = clipName.LastIndexOfAny(Separators);
        string bare = cut >= 0 && cut < clipName.Length - 1 ? clipName[(cut + 1)..] : clipName;

        // ...and a gendered pack prefix. Some packs name every clip for the body rather than for the
        // beat — npc_woman_dress ships "HumanArmature|Female_Idle", "Female_Run", "Female_SwordSlash"
        // — and that prefix silently emptied EVERY slot she has: she had been standing in the
        // Embermarket in her bind pose, which reads as a T-posing merchant and logs nothing.
        // Stripping it here rather than adding a "female_" twin for every alias means the next
        // gendered pack works on import, which is the same bargain the armature prefix made.
        foreach (string sex in Sexes)
        {
            if (bare.StartsWith(sex, StringComparison.OrdinalIgnoreCase) && bare.Length > sex.Length)
            {
                return bare[sex.Length..];
            }
        }

        return bare;
    }

    private static readonly char[] Separators = { '|', '/' };

    private static readonly string[] Sexes = { "Female_", "Male_" };
}
