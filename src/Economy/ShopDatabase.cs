using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

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
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"ShopDatabase: directory '{directory}' not found; no shops loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var shop = GD.Load<ShopResource>($"{directory}/{name}");
            if (shop == null)
            {
                continue;
            }

            if (ById.ContainsKey(shop.Id))
            {
                Log.Warn($"Duplicate shop id '{shop.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(shop);
            }

            ById[shop.Id] = shop;
        }

        Log.Info($"ShopDatabase loaded {ById.Count} shop(s) from {directory}.");
    }

    public static ShopResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out ShopResource? shop) ? shop : null;
}
