using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="EncounterResource"/>s, scanned once at startup
/// from <c>res://data/encounters</c> (mirrors the established database pattern). The
/// <see cref="EncounterDirector"/> filters <see cref="All"/> by the current day phase
/// and picks one by weight. New encounter = drop a <c>.tres</c>, no code change.
/// </summary>
public static class EncounterDatabase
{
    private const string DefaultDirectory = "res://data/encounters";

    private static readonly Dictionary<string, EncounterResource> ById = new();
    private static readonly List<EncounterResource> AllList = new();

    public static IReadOnlyList<EncounterResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<EncounterResource>(
            directory, "encounter", encounter => encounter.Id, ById, AllList);
    }

    public static EncounterResource? Get(string id)
    {
        return ById.TryGetValue(id, out EncounterResource? encounter) ? encounter : null;
    }
}
