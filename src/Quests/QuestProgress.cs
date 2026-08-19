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
        SecondsLeft = quest.TimeLimitSeconds;

        List<ObjectiveResource> objectives = quest.ObjectiveList();
        Counts = new int[objectives.Count];

        // ⚠️ A Stealth objective starts MET (41C). It is a condition rather than a task — there is
        // nothing to do to "achieve" not being seen — so seeding it here is what lets AllObjectivesMet
        // and every counting surface stay exactly as they were. The alternative, a special case in
        // AllObjectivesMet, would put the rule somewhere nothing reading Counts can see it.
        for (int i = 0; i < objectives.Count; i++)
        {
            if (objectives[i].Type == ObjectiveType.Stealth)
            {
                Counts[i] = objectives[i].RequiredCount;
            }
        }
    }

    public QuestResource Quest { get; }

    /// <summary>Progress toward each objective, indexed to <see cref="QuestResource.ObjectiveList"/>.</summary>
    public int[] Counts { get; }

    public QuestStatus Status { get; set; } = QuestStatus.Active;

    /// <summary>Seconds left on a timed quest's deadline (41C), counted down by
    /// <see cref="QuestLogComponent"/>. Meaningless when <see cref="QuestResource.TimeLimitSeconds"/>
    /// is 0, and <see cref="IsTimed"/> is the question to ask rather than comparing this to zero.</summary>
    public float SecondsLeft { get; set; }

    public bool IsTimed => Quest.TimeLimitSeconds > 0f;

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
            ["left"] = SecondsLeft,
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

        // ⚠️ Absent means an old save, not "no time left" — a missing key must restore the full
        // deadline rather than zero, or every timed quest in a pre-41C save fails on the first tick
        // after loading. The constructor has already seeded it from the resource, so the absent case
        // is simply left alone (CLAUDE.md §7: ask what happens when the saved value is 0 or missing).
        if (data.TryGetValue("left", out Variant leftVar))
        {
            progress.SecondsLeft = (float)leftVar.AsDouble();
        }

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
