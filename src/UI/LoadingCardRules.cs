using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>
/// What the loading screen shows that depends on where the player is going and where they have
/// been: the realm's painting, and the one tip or lore line under its name. Pure, so the rule that
/// matters is a unit test: nothing here can show a place the player has not reached.
///
/// ⚠️ The hidden realm (<see cref="HiddenRealmId"/>) has a painting and no lore set. Its painting
/// is returned only for a load into that realm, and no card names it, visited or not: a card is
/// read on the way to somewhere else, over a companion's shoulder or on a stream.
/// </summary>
public static class LoadingCardRules
{
    /// <summary>The realm that is on no map until the story reveals it.</summary>
    public const string HiddenRealmId = "region.pale_concord";

    /// <summary>General tips, <c>loading.tip.1</c> to this number. Shown anywhere.</summary>
    public const int TipCount = 12;

    private const string TipPrefix = "loading.tip.";
    private const string LorePrefix = "loading.lore.";

    /// <summary>Realms with lore lines: the region id, the slug its keys carry, its painting and
    /// how many lines it has (<c>loading.lore.&lt;slug&gt;.1</c> onward).</summary>
    private static readonly (string RegionId, string Slug, string Painting, int Lines)[] Realms =
    {
        ("region.ember_crown", "ember_crown", "loading_ember_crown", 3),
        ("region.frostfang_reach", "frostfang", "loading_frostfang", 3),
        ("region.ashen_wilds", "ashen_wilds", "loading_ashen_wilds", 3),
        ("region.sunspire", "sunspire", "loading_sunspire", 3),
        ("region.celestial", "celestial", "loading_celestial", 2),
    };

    /// <summary>The painting (a file name under <c>assets/ui/backgrounds/</c>) for a load into
    /// <paramref name="regionId"/>; the generic one for no region or one with no painting.</summary>
    public static string Painting(string? regionId)
    {
        if (regionId == HiddenRealmId)
        {
            return "loading_pale";
        }

        foreach ((string id, _, string painting, _) in Realms)
        {
            if (id == regionId)
            {
                return painting;
            }
        }

        return UiTheme.GenericPainting;
    }

    /// <summary>Every card that may be shown to a player who has been to
    /// <paramref name="visited"/>: all the tips, then the lore of each realm on that list.</summary>
    public static List<string> Candidates(IEnumerable<string>? visited)
    {
        var keys = new List<string>(TipCount + 6);
        for (int i = 1; i <= TipCount; i++)
        {
            keys.Add(TipPrefix + i);
        }

        if (visited == null)
        {
            return keys;
        }

        var seen = new HashSet<string>(visited, StringComparer.Ordinal);
        foreach ((string id, string slug, _, int lines) in Realms)
        {
            if (!seen.Contains(id))
            {
                continue;
            }

            for (int i = 1; i <= lines; i++)
            {
                keys.Add($"{LorePrefix}{slug}.{i}");
            }
        }

        return keys;
    }

    /// <summary>
    /// The card for this load. <paramref name="seed"/> is any number that differs between loads;
    /// <paramref name="previous"/> is the card shown last time, which is not shown twice running.
    /// </summary>
    public static string Pick(IEnumerable<string>? visited, int seed, string? previous = null)
    {
        List<string> keys = Candidates(visited);
        if (previous != null && keys.Count > 1)
        {
            keys.Remove(previous);
        }

        return keys[(int)((uint)seed % (uint)keys.Count)];
    }

    /// <summary>Whether a card is a lore line (it is headed "Lore") or a tip.</summary>
    public static bool IsLore(string key) => key.StartsWith(LorePrefix, StringComparison.Ordinal);
}
