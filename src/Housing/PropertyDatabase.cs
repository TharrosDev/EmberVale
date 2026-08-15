using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Housing;

/// <summary>
/// Process-wide registry of <see cref="PropertyResource"/>s, scanned once at startup from
/// <c>res://data/properties</c> (the established database pattern — a direct mirror of
/// <see cref="Enemies.BossDatabase"/>). A <see cref="PropertyDeedComponent"/> names one by id, so a
/// new holding is a <c>.tres</c> and a deed placed in a cell, with no code.
/// </summary>
public static class PropertyDatabase
{
    private const string DefaultDirectory = "res://data/properties";

    private static readonly Dictionary<string, PropertyResource> ById = new();
    private static readonly List<PropertyResource> AllList = new();

    public static IReadOnlyList<PropertyResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<PropertyResource>(
            directory, "property", property => property.Id, ById, AllList);
    }

    public static PropertyResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out PropertyResource? property) ? property : null;
}
