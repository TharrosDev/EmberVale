using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="BossResource"/>s, scanned once at startup from
/// <c>res://data/bosses</c> (the established database pattern). An archetype names one through
/// <see cref="EnemyArchetypeResource.BossId"/>, so dropping a <c>.tres</c> in the folder and naming
/// it is all a boss's phase structure needs — no code, and the content validator cross-checks the
/// reference in both directions.
///
/// Initialized <b>before</b> <see cref="EnemyArchetypeDatabase"/> so that cross-check can run.
/// </summary>
public static class BossDatabase
{
    private const string DefaultDirectory = "res://data/bosses";

    private static readonly Dictionary<string, BossResource> ById = new();
    private static readonly List<BossResource> AllList = new();

    public static IReadOnlyList<BossResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"BossDatabase: directory '{directory}' not found; no bosses loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var boss = GD.Load<BossResource>($"{directory}/{name}");
            if (boss == null)
            {
                continue;
            }

            if (ById.ContainsKey(boss.Id))
            {
                Log.Warn($"Duplicate boss id '{boss.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(boss);
            }

            ById[boss.Id] = boss;
        }

        Log.Info($"BossDatabase loaded {ById.Count} boss(es) from {directory}.");
    }

    public static BossResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out BossResource? boss) ? boss : null;
}
