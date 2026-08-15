using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Dialogue;

/// <summary>
/// Process-wide registry of <see cref="DialogueResource"/>s, scanned once at startup
/// from <c>res://data/dialogue</c> (mirrors <see cref="Embervale.Quests.QuestDatabase"/>
/// and the other content databases). NPCs resolve their conversation by stable string
/// id. New conversation = drop a <c>.tres</c>, no code change.
/// </summary>
public static class DialogueDatabase
{
    private const string DefaultDirectory = "res://data/dialogue";

    private static readonly Dictionary<string, DialogueResource> ById = new();
    private static readonly List<DialogueResource> AllList = new();

    public static IReadOnlyList<DialogueResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<DialogueResource>(
            directory, "dialogue", dialogue => dialogue.Id, ById, AllList);
    }

    public static DialogueResource? Get(string id)
    {
        return ById.TryGetValue(id, out DialogueResource? dialogue) ? dialogue : null;
    }
}
