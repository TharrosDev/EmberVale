using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Factions;

/// <summary>
/// Process-wide registry of <see cref="FactionResource"/>s, scanned once at startup
/// from <c>res://data/factions</c> (mirrors the established database pattern). The
/// <see cref="ReputationComponent"/> seeds its standings from <see cref="All"/> and
/// resolves a faction by id; <see cref="FactionComponent"/> tags actors. New faction =
/// drop a <c>.tres</c>, no code change.
/// </summary>
public static class FactionDatabase
{
    private const string DefaultDirectory = "res://data/factions";

    private static readonly Dictionary<string, FactionResource> ById = new();
    private static readonly List<FactionResource> AllList = new();

    public static IReadOnlyList<FactionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<FactionResource>(
            directory, "faction", faction => faction.Id, ById, AllList);
    }

    public static FactionResource? Get(string id)
    {
        return ById.TryGetValue(id, out FactionResource? faction) ? faction : null;
    }
}
