using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Economy;

/// <summary>
/// Process-wide registry of <see cref="ContractResource"/>s, scanned once at startup from
/// <c>res://data/contracts</c> — a direct mirror of <see cref="ServiceDatabase"/>, so a new posting is
/// a <c>.tres</c> and nothing else.
///
/// <b><see cref="All"/>'s order is the pool order the board indexes into</b>, and it is the file order
/// the directory scan returns. That is stable for a given set of files, which is all
/// <see cref="ContractRules.SlotContract"/> needs — but ⚠️ <b>adding or removing a contract reshuffles
/// which posting sits on which slot for every past and future cycle</b>. Nothing breaks (the board is
/// derived, not saved) and a player would see the board change under them exactly once. Worth knowing
/// before blaming the rotation for being wrong.
/// </summary>
public static class ContractDatabase
{
    private const string DefaultDirectory = "res://data/contracts";

    private static readonly Dictionary<string, ContractResource> ById = new();
    private static readonly List<ContractResource> AllList = new();

    public static IReadOnlyList<ContractResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<ContractResource>(
            directory, "contract", contract => contract.Id, ById, AllList);
    }

    public static ContractResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out ContractResource? contract) ? contract : null;
}
