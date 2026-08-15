using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Economy;

/// <summary>
/// Process-wide registry of <see cref="ShopResource"/>s, scanned once at startup from
/// <c>res://data/shops</c> — a direct mirror of <see cref="Housing.PropertyDatabase"/>. A
/// <see cref="VendorComponent"/> names one by id, so a new shop is a <c>.tres</c> and nothing else.
/// </summary>
public static class ShopDatabase
{
    private const string DefaultDirectory = "res://data/shops";

    private static readonly Dictionary<string, ShopResource> ById = new();
    private static readonly List<ShopResource> AllList = new();

    public static IReadOnlyList<ShopResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<ShopResource>(
            directory, "shop", shop => shop.Id, ById, AllList);
    }

    public static ShopResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out ShopResource? shop) ? shop : null;
}
