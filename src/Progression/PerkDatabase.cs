using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

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
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"PerkDatabase: directory '{directory}' not found; no perks loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var perk = GD.Load<PerkResource>($"{directory}/{name}");
            if (perk == null)
            {
                continue;
            }

            if (ById.ContainsKey(perk.Id))
            {
                Log.Warn($"Duplicate perk id '{perk.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(perk);
            }

            ById[perk.Id] = perk;
        }

        Log.Info($"PerkDatabase loaded {ById.Count} perk(s) from {directory}.");
    }

    public static PerkResource? Get(string id)
    {
        return ById.TryGetValue(id, out PerkResource? perk) ? perk : null;
    }
}
