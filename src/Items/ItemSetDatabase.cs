using System.Collections.Generic;
using Embervale.Core;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Process-wide registry of <see cref="ItemSetResource"/>s, scanned at startup from
/// <c>res://data/item_sets</c> (mirrors <see cref="AffixDatabase"/>). The folder is allowed to be
/// absent: a build with no sets authored yet has an empty database, not a boot warning.
/// </summary>
public static class ItemSetDatabase
{
    private const string DefaultDirectory = "res://data/item_sets";

    private static readonly Dictionary<string, ItemSetResource> ById = new();
    private static readonly List<ItemSetResource> AllList = new();

    public static IReadOnlyList<ItemSetResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            ById.Clear();
            AllList.Clear();
            return;
        }

        ResourceDirectory.Load<ItemSetResource>(
            directory, "item set", set => set.Id, ById, AllList);
    }

    public static ItemSetResource? Get(string id)
    {
        return !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out ItemSetResource? set) ? set : null;
    }
}
