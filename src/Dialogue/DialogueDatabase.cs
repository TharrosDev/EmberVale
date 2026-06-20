using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

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
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"DialogueDatabase: directory '{directory}' not found; no dialogue loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var dialogue = GD.Load<DialogueResource>($"{directory}/{name}");
            if (dialogue == null)
            {
                continue;
            }

            if (ById.ContainsKey(dialogue.Id))
            {
                Log.Warn($"Duplicate dialogue id '{dialogue.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(dialogue);
            }

            ById[dialogue.Id] = dialogue;
        }

        Log.Info($"DialogueDatabase loaded {ById.Count} conversation(s) from {directory}.");
    }

    public static DialogueResource? Get(string id)
    {
        return ById.TryGetValue(id, out DialogueResource? dialogue) ? dialogue : null;
    }
}
