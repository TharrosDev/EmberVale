using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="MapLocationResource"/>s (Phase 39.5A), scanned once at
/// startup from <c>res://data/map_locations</c> — a direct mirror of
/// <see cref="Embervale.Economy.ServiceDatabase"/>, so a new map location is a <c>.tres</c> and
/// nothing else.
/// </summary>
public static class MapLocationDatabase
{
    private const string DefaultDirectory = "res://data/map_locations";

    private static readonly Dictionary<string, MapLocationResource> ById = new();
    private static readonly List<MapLocationResource> AllList = new();

    public static IReadOnlyList<MapLocationResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<MapLocationResource>(
            directory, "map location", location => location.Id, ById, AllList);
    }

    public static MapLocationResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out MapLocationResource? location) ? location : null;
}
