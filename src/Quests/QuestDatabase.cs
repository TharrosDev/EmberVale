using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Quests;

/// <summary>
/// Process-wide registry of <see cref="QuestResource"/>s, scanned once at startup from
/// <c>res://data/quests</c> (mirrors <see cref="Embervale.Items.ItemDatabase"/> and the
/// other content databases). Persistence and quest-givers resolve quests by their
/// stable string id. New quest = drop a <c>.tres</c>, no code change.
/// </summary>
public static class QuestDatabase
{
    private const string DefaultDirectory = "res://data/quests";

    private static readonly Dictionary<string, QuestResource> ById = new();
    private static readonly List<QuestResource> AllList = new();

    public static IReadOnlyList<QuestResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"QuestDatabase: directory '{directory}' not found; no quests loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var quest = GD.Load<QuestResource>($"{directory}/{name}");
            if (quest == null)
            {
                continue;
            }

            if (ById.ContainsKey(quest.Id))
            {
                Log.Warn($"Duplicate quest id '{quest.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(quest);
            }

            ById[quest.Id] = quest;
        }

        Log.Info($"QuestDatabase loaded {ById.Count} quest(s) from {directory}.");
    }

    public static QuestResource? Get(string id)
    {
        return ById.TryGetValue(id, out QuestResource? quest) ? quest : null;
    }
}
