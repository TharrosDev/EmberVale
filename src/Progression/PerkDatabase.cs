using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Progression;

/// <summary>
/// Process-wide registry of <see cref="PerkResource"/>s, scanned once at startup
/// from <c>res://data/perks</c> (mirrors <see cref="Embervale.Items.ItemDatabase"/>).
/// The perks UI lists <see cref="All"/>; <see cref="PerksComponent"/> resolves a
/// learned perk back by id on load. New perk = drop a <c>.tres</c>, no code change.
/// </summary>
public static class PerkDatabase
{
    private const string DefaultDirectory = "res://data/perks";

    private static readonly Dictionary<string, PerkResource> ById = new();
    private static readonly List<PerkResource> AllList = new();

    public static IReadOnlyList<PerkResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<PerkResource>(
            directory, "perk", perk => perk.Id, ById, AllList);
    }

    public static PerkResource? Get(string id)
    {
        return ById.TryGetValue(id, out PerkResource? perk) ? perk : null;
    }
}
