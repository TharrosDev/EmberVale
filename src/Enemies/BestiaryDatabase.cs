using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="BestiaryEntryResource"/>s, scanned once at startup from
/// <c>res://data/bestiary</c> (the established database pattern). The bestiary screen reads its
/// pages from here, so adding a creature's lore is a <c>.tres</c> rather than a rebuild.
/// </summary>
public static class BestiaryDatabase
{
    private const string DefaultDirectory = "res://data/bestiary";

    private static readonly Dictionary<string, BestiaryEntryResource> ById = new();
    private static readonly List<BestiaryEntryResource> AllList = new();

    public static IReadOnlyList<BestiaryEntryResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<BestiaryEntryResource>(
            directory, "bestiary entry", entry => entry.Id, ById, AllList);
    }

    public static BestiaryEntryResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out BestiaryEntryResource? entry) ? entry : null;

    public static bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && ById.ContainsKey(id);
}
