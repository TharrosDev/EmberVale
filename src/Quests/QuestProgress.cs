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

    /// <summary>
    /// How this progress answers "is that story flag set" (41D) — supplied by
    /// <see cref="QuestLogComponent"/>, which owns the actor and therefore the
    /// <c>StoryFlagsComponent</c>.
    ///
    /// ⚠️ <b>NULL MEANS EVERY GATE IS OPEN, and that default is load-bearing.</b> A
    /// <see cref="QuestProgress"/> built anywhere else — a harness, a future headless report — then
    /// behaves exactly as it did before branching existed, rather than seeing every gated objective
    /// as inert and completing quests that have not been done. The failure of the other default is
    /// silent and grants rewards, which is the worse of the two by a long way.
    ///
    /// A delegate rather than the component itself so the branch rule stays free of Godot types and
    /// the objective predicates below remain testable.
    /// </summary>
    public System.Func<string, bool>? HasFlag { get; set; }

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

    /// <summary>
    /// Whether objective <paramref name="index"/> counts at all right now (41D): its branch gate is
    /// open, and — on a <see cref="QuestResource.SequentialObjectives"/> quest — every earlier live
    /// objective is done.
    ///
    /// ⚠️ <b>THIS IS THE SINGLE ANSWER TO "DOES THIS OBJECTIVE COUNT", AND EVERY SURFACE MUST ASK
    /// IT.</b> An inert objective is neither complete nor pending, which is a third state the whole
    /// codebase was written without — so every existing filter spelled <c>!IsObjectiveComplete(i)</c>
    /// is wrong about it by default, and wrong in the loudest possible way: the compass, the map pin
    /// and the tracker would each aim the player at the branch they did not take. 41C predicted this
    /// exact shape one type early (*a seeded state is invisible to every rule written for an earned
    /// one*); this is its mirror.
    /// </summary>
    public bool IsObjectiveActive(int index)
    {
        List<ObjectiveResource> objectives = Quest.ObjectiveList();
        if (index < 0 || index >= objectives.Count)
        {
            return false;
        }

        // Common case by a wide margin: nothing on this quest is gated and it is not ordered, so no
        // array is built and no flag is read.
        if (!Quest.SequentialObjectives && !objectives[index].IsGated)
        {
            return true;
        }

        return ObjectiveProgress.IsActive(
            index, GateStates(objectives), Counts, Required(objectives), Quest.SequentialObjectives);
    }

    /// <summary>
    /// Whether objective <paramref name="index"/> belongs to the path this save is on (41D) — its
    /// branch gate alone, ignoring sequential locking.
    ///
    /// ⚠️ <b>The two questions are different and the UI needs both.</b> A gate-shut objective belongs
    /// to the branch the player did NOT take and is hidden outright — drawing it would advertise a
    /// path they can no longer reach. An objective that is in-branch but not yet active is merely
    /// LOCKED behind an earlier step, and hiding that one would make a three-step errand look like a
    /// one-step errand and its journal card look wrong when a row appears from nowhere.
    /// </summary>
    public bool IsObjectiveInBranch(int index)
    {
        List<ObjectiveResource> objectives = Quest.ObjectiveList();
        return index >= 0 && index < objectives.Count && objectives[index].IsGateOpen(HasFlag);
    }

    /// <summary>
    /// True when every LIVE objective has met its required count — and at least one IS live.
    ///
    /// ⚠️ <b>The "at least one" half is not a nicety.</b> A quest whose objectives all belong to
    /// branches has nothing live until a flag chooses a path, and an all-inert quest would otherwise
    /// be vacuously complete: accepted and finished on the same frame, with rewards, through a green
    /// build. See <see cref="ObjectiveProgress.AllLiveMet"/>.
    /// </summary>
    public bool AllObjectivesMet()
    {
        List<ObjectiveResource> objectives = Quest.ObjectiveList();
        return ObjectiveProgress.AllLiveMet(
            GateStates(objectives), Counts, Required(objectives), Quest.SequentialObjectives);
    }

    /// <summary>Each objective's branch-gate state, resolved through <see cref="HasFlag"/>.</summary>
    private bool[] GateStates(List<ObjectiveResource> objectives)
    {
        var open = new bool[objectives.Count];
        for (int i = 0; i < objectives.Count; i++)
        {
            open[i] = objectives[i].IsGateOpen(HasFlag);
        }

        return open;
    }

    private static int[] Required(List<ObjectiveResource> objectives)
    {
        var required = new int[objectives.Count];
        for (int i = 0; i < objectives.Count; i++)
        {
            required[i] = objectives[i].RequiredCount;
        }

        return required;
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
