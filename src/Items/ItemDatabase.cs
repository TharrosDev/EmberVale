using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Items;

/// <summary>
/// Process-wide registry mapping item ids to their <see cref="ItemResource"/>
/// templates. Populated once at startup by scanning <c>res://data/items</c>, it
/// lets persistence and loot resolve items by their stable string id rather than
/// hard references — so new items are added by dropping a <c>.tres</c> in the
/// folder, no code change required.
/// </summary>
public static class ItemDatabase
{
    private const string DefaultDirectory = "res://data/items";

    private static readonly Dictionary<string, ItemResource> ById = new();

    public static IReadOnlyDictionary<string, ItemResource> All => ById;

    /// <summary>Scans the items directory and (re)builds the id → template map.</summary>
    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<ItemResource>(
            directory, "item", item => item.Id, ById);
    }

    public static ItemResource? Get(string id)
    {
        return ById.TryGetValue(id, out ItemResource? item) ? item : null;
    }

    public static bool TryGet(string id, out ItemResource item)
    {
        return ById.TryGetValue(id, out item!);
    }
}
