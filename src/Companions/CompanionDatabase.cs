using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Companions;

/// <summary>
/// Process-wide registry of <see cref="CompanionResource"/>s, scanned once at startup from
/// <c>res://data/companions</c> (the established database pattern). <see cref="CompanionRegistry"/>
/// seeds its archetypes from <see cref="All"/>, so dropping a <c>.tres</c> in the folder is all a
/// new companion needs on the code side.
/// </summary>
public static class CompanionDatabase
{
    private const string DefaultDirectory = "res://data/companions";

    private static readonly Dictionary<string, CompanionResource> ById = new();
    private static readonly List<CompanionResource> AllList = new();

    public static IReadOnlyList<CompanionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<CompanionResource>(
            directory, "companion", companion => companion.Id, ById, AllList);
    }

    public static CompanionResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out CompanionResource? companion) ? companion : null;
}
