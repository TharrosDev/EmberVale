using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Embervale.Bootstrap;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The guard on <see cref="SessionLifecycleCoordinator.ResetSessionStatics"/>.
///
/// <para>Quitting to the title used to reload the entire scene, which cleared every process-lifetime
/// static as a side effect. Nothing does that any more — a session is a node now, and freeing it
/// takes its subtree and its service scopes but cannot touch a <c>static</c>. So the reset list is
/// hand-written, and a hand-written list of "everything of a kind" rots the moment someone adds one
/// more of that kind.</para>
///
/// <para>This test finds them by reflection instead of trusting the list: any public static class in
/// the game assembly exposing a parameterless <c>Clear</c>, <c>Reset</c> or <c>ClearAll</c> is
/// session-scoped process state by construction, and must be accounted for. Adding one and
/// forgetting to reset it fails here rather than leaking into the next playthrough — which is a bug
/// that would present as "the second game I start behaves like the first one".</para>
/// </summary>
public class SessionResetTests
{
    /// <summary>
    /// The statics <c>ResetSessionStatics</c> clears. Kept in sync with that method by hand — this
    /// list is the assertion, not the source of truth.
    /// </summary>
    private static readonly HashSet<string> Reset = new(StringComparer.Ordinal)
    {
        "Invariant",                // violation counter — a new session starts at zero
        "PersistentActorRegistry",  // template factories, re-registered by the next session's build
        "SafeZones",                // per-region sanctuary set
        "SpellActions",             // cast timelines derived per spell id; the database is rebuilt on a new game
        "UiState",                  // open panels and world pausers; a stale pauser locks the title
        "Weave",                    // the active region's magic potency
    };

    /// <summary>
    /// Statics that are deliberately NOT reset, with the reason. Empty today; an entry here is a
    /// decision someone made, which is the point of making it explicit rather than implicit.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyKept = new(StringComparer.Ordinal);

    [Fact]
    public void EveryResettableStaticIsAccountedFor()
    {
        List<string> found = ResettableStatics().ToList();

        Assert.NotEmpty(found); // reflection silently finding nothing would pass everything

        List<string> unaccounted = found
            .Where(name => !Reset.Contains(name) && !DeliberatelyKept.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unaccounted.Count == 0,
            "These static classes hold process-lifetime state that no session teardown clears: " +
            string.Join(", ", unaccounted) +
            ". Add each to SessionLifecycleCoordinator.ResetSessionStatics (and to this test's Reset " +
            "set), or to DeliberatelyKept with the reason it outlives a session.");
    }

    [Fact]
    public void TheResetListNamesOnlyStaticsThatStillExist()
    {
        // The other direction: a static that was renamed or deleted leaves a dead name in the list,
        // and a list with a dead name in it is a list nobody trusts.
        HashSet<string> found = ResettableStatics().ToHashSet(StringComparer.Ordinal);
        List<string> stale = Reset.Where(name => !found.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0, "The reset list names statics that no longer exist: " + string.Join(", ", stale));
    }

    private static IEnumerable<string> ResettableStatics()
    {
        Type[] types;
        try
        {
            types = typeof(SessionLifecycleCoordinator).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray()!;
        }

        foreach (Type type in types)
        {
            // A static class in C# is abstract + sealed.
            if (!type.IsClass || !type.IsAbstract || !type.IsSealed || !type.IsPublic)
            {
                continue;
            }

            bool resettable = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name is "Clear" or "Reset" or "ClearAll"
                    && m.GetParameters().Length == 0
                    && m.ReturnType == typeof(void));

            if (resettable)
            {
                yield return type.Name;
            }
        }
    }
}
