using System.Collections.Generic;
using Embervale.Core;

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
        ResourceDirectory.Load<QuestResource>(
            directory, "quest", quest => quest.Id, ById, AllList);
    }

    public static QuestResource? Get(string id)
    {
        return ById.TryGetValue(id, out QuestResource? quest) ? quest : null;
    }
}
