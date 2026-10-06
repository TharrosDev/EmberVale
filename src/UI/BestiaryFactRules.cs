using System.Collections.Generic;
using Embervale.Enemies;

namespace Embervale.UI;

/// <summary>One school's resistance on a creature. <paramref name="School"/> is the
/// <c>DamageType</c> ordinal.</summary>
public readonly record struct SchoolResist(int School, float Value);

/// <summary>
/// What a bestiary page tells the player at each stage, and which schools a creature's
/// resistances make worth naming. Pure: the panel hands it numbers and lays the answer out.
///
/// A creature has no negative resistance in this game, so "open to" is relative: the schools it
/// has no ward against at all, named only when it wards against something else.
/// </summary>
public static class BestiaryFactRules
{
    /// <summary>Chips shown per row. More than this and the row stops being a glance.</summary>
    public const int MaxChips = 3;

    /// <summary>The name is written on the first kill.</summary>
    public static bool ShowsName(BestiaryStage stage) => stage != BestiaryStage.Unseen;

    /// <summary>The tally and the count still needed, while the page is part written.</summary>
    public static bool ShowsProgress(BestiaryStage stage) => stage == BestiaryStage.Sighted;

    /// <summary>Lore, resistances and openings belong to the finished page.</summary>
    public static bool ShowsStudy(BestiaryStage stage) => stage == BestiaryStage.Known;

    /// <summary>The schools a creature wards against enough to matter: at least half its best
    /// ward, strongest first.</summary>
    public static List<int> Resists(IReadOnlyList<SchoolResist> all)
    {
        float best = Best(all);
        var picked = new List<SchoolResist>();
        foreach (SchoolResist entry in all)
        {
            if (best <= 0f || entry.Value <= 0f || entry.Value < best * 0.5f)
            {
                continue;
            }

            // Inserted after every ward at least as strong, so equal wards keep the order given.
            int at = picked.Count;
            while (at > 0 && picked[at - 1].Value < entry.Value)
            {
                at--;
            }

            picked.Insert(at, entry);
        }

        return Schools(picked);
    }

    /// <summary>The schools a creature has no ward against, when it has one against another.</summary>
    public static List<int> OpenTo(IReadOnlyList<SchoolResist> all)
    {
        var picked = new List<SchoolResist>();
        if (Best(all) <= 0f)
        {
            return Schools(picked);
        }

        foreach (SchoolResist entry in all)
        {
            if (entry.Value <= 0f)
            {
                picked.Add(entry);
            }
        }

        return Schools(picked);
    }

    private static float Best(IReadOnlyList<SchoolResist> all)
    {
        float best = 0f;
        foreach (SchoolResist entry in all)
        {
            if (entry.Value > best)
            {
                best = entry.Value;
            }
        }

        return best;
    }

    private static List<int> Schools(List<SchoolResist> picked)
    {
        var schools = new List<int>();
        for (int i = 0; i < picked.Count && i < MaxChips; i++)
        {
            schools.Add(picked[i].School);
        }

        return schools;
    }
}
