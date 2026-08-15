using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Races;

/// <summary>
/// Process-wide registry of <see cref="RaceResource"/>s, scanned once at startup from
/// <c>res://data/races</c> (mirrors <see cref="Embervale.Progression.PerkDatabase"/>). The character
/// creator (Phase 26D) lists <see cref="All"/>; <c>PlayerFactory</c> (26C) and the save header resolve a
/// chosen race back by id. New race = drop a <c>.tres</c>, no code change.
/// </summary>
public static class RaceDatabase
{
    private const string DefaultDirectory = "res://data/races";

    private static readonly Dictionary<string, RaceResource> ById = new();
    private static readonly List<RaceResource> AllList = new();

    public static IReadOnlyList<RaceResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<RaceResource>(
            directory, "race", race => race.Id, ById, AllList);
    }

    public static RaceResource? Get(string id)
    {
        return ById.TryGetValue(id, out RaceResource? race) ? race : null;
    }
}
