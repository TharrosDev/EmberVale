using System.Collections.Generic;
using Godot;

namespace Embervale.Quests;

/// <summary>
/// Runtime tracking of one quest for one actor: the authored <see cref="QuestResource"/>
/// plus a per-objective progress count and the current <see cref="QuestStatus"/>.
/// Plain C# (not a Godot resource) — it is owned by the <see cref="QuestLogComponent"/>
/// and serialized into the save dictionary.
/// </summary>
public sealed class QuestProgress
{
    public QuestProgress(QuestResource quest)
    {
        Quest = quest;
        Counts = new int[quest.ObjectiveList().Count];
    }

    public QuestResource Quest { get; }

    /// <summary>Progress toward each objective, indexed to <see cref="QuestResource.ObjectiveList"/>.</summary>
    public int[] Counts { get; }

    public QuestStatus Status { get; set; } = QuestStatus.Active;

    public bool IsObjectiveComplete(int index)
    {
        List<ObjectiveResource> objectives = Quest.ObjectiveList();
        return index >= 0 && index < objectives.Count
            && ObjectiveProgress.IsComplete(Counts[index], objectives[index].RequiredCount);
    }

    /// <summary>True when every objective has met its required count.</summary>
    public bool AllObjectivesMet()
    {
        List<ObjectiveResource> objectives = Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (!ObjectiveProgress.IsComplete(Counts[i], objectives[i].RequiredCount))
            {
                return false;
            }
        }

        return true;
    }

    public Godot.Collections.Dictionary Save()
    {
        var counts = new Godot.Collections.Array();
        foreach (int c in Counts)
        {
            counts.Add(c);
        }

        return new Godot.Collections.Dictionary
        {
            ["id"] = Quest.Id,
            ["status"] = (int)Status,
            ["counts"] = counts,
        };
    }

    /// <summary>Rebuilds progress from saved state, resolving the quest by id.
    /// Returns null if the quest no longer exists.</summary>
    public static QuestProgress? FromSave(Godot.Collections.Dictionary data)
    {
        string id = data["id"].AsString();
        QuestResource? quest = QuestDatabase.Get(id);
        if (quest == null)
        {
            return null;
        }

        var progress = new QuestProgress(quest)
        {
            Status = (QuestStatus)data["status"].AsInt32(),
        };

        if (data.TryGetValue("counts", out Variant countsVar))
        {
            Godot.Collections.Array counts = countsVar.AsGodotArray();
            for (int i = 0; i < progress.Counts.Length && i < counts.Count; i++)
            {
                progress.Counts[i] = counts[i].AsInt32();
            }
        }

        return progress;
    }
}
