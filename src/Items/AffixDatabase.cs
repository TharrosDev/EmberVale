using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Process-wide registry of <see cref="AffixDefinition"/>s, scanned once at startup
/// from <c>res://data/affixes</c> (mirrors <see cref="ItemDatabase"/>). The loot
/// generator queries it for the pool of affixes eligible for a given equippable at
/// a given rarity; persistence resolves nothing here (rolled values live on the
/// instance), but the id is kept so future tooling can look definitions back up.
/// </summary>
public static class AffixDatabase
{
    private const string DefaultDirectory = "res://data/affixes";

    private static readonly Dictionary<string, AffixDefinition> ById = new();
    private static readonly List<AffixDefinition> AllList = new();

    public static IReadOnlyList<AffixDefinition> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"AffixDatabase: directory '{directory}' not found; no affixes loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var affix = GD.Load<AffixDefinition>($"{directory}/{name}");
            if (affix == null)
            {
                continue;
            }

            if (ById.ContainsKey(affix.Id))
            {
                Log.Warn($"Duplicate affix id '{affix.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(affix);
            }

            ById[affix.Id] = affix;
        }

        Log.Info($"AffixDatabase loaded {ById.Count} affix(es) from {directory}.");
    }

    public static AffixDefinition? Get(string id)
    {
        return ById.TryGetValue(id, out AffixDefinition? affix) ? affix : null;
    }

    /// <summary>All affix definitions that may roll on <paramref name="item"/> at
    /// the given <paramref name="rarity"/>.</summary>
    public static List<AffixDefinition> ApplicableTo(EquippableItemResource item, ItemRarity rarity)
    {
        var pool = new List<AffixDefinition>();
        foreach (AffixDefinition def in AllList)
        {
            if (def.AppliesTo(item, rarity))
            {
                pool.Add(def);
            }
        }

        return pool;
    }
}
