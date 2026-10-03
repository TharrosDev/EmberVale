using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>What a quest toast announces. Order is priority, lowest first.</summary>
public enum QuestNoticeKind
{
    /// <summary>A required step was met and nothing new opened.</summary>
    Updated,

    /// <summary>An optional objective was met.</summary>
    OptionalDone,

    /// <summary>A required objective opened.</summary>
    NewObjective,

    /// <summary>The quest began.</summary>
    Started,

    /// <summary>The quest was lost.</summary>
    Failed,

    /// <summary>The quest was finished.</summary>
    Completed,
}

/// <summary>One toast to show. <paramref name="ObjectiveIndex"/> is the objective the line names (-1 for none).
/// <paramref name="CompletedIndex"/> is the required step that was met alongside, when any.</summary>
public readonly record struct QuestNoticeIntent(
    QuestNoticeKind Kind, string QuestId, int ObjectiveIndex, int CompletedIndex);

/// <summary>
/// Collapses one frame's worth of quest events into the toasts worth showing. A single player action
/// publishes several events (the objective advanced, the stage changed, the next objective activated, and
/// sometimes the quest completed); announcing each reads as a bug, so every quest yields at most one
/// required-flow toast per flush, plus one per optional objective met:
/// <list type="bullet">
/// <item>Completed outranks everything else for that quest.</item>
/// <item>Failed outranks everything but Completed.</item>
/// <item>Started absorbs the first objective's activation (shown as the toast's second line).</item>
/// <item>Otherwise an activation beats a plain "updated"; a step met together with a new activation
/// is ONE toast naming the next objective.</item>
/// </list>
/// Events are fed in as they arrive and <see cref="Flush"/> is called once per frame.
/// </summary>
public sealed class QuestNoticeCoalescer
{
    private sealed class Pending
    {
        public bool Started;
        public bool Completed;
        public bool Failed;
        public int Activated = -1;
        public int RequiredDone = -1;
        public readonly List<int> OptionalDone = new();
    }

    private readonly Dictionary<string, Pending> _pending = new();
    private readonly List<string> _order = new();

    public bool HasPending => _pending.Count > 0;

    public void OnStarted(string questId) => Of(questId).Started = true;

    public void OnCompleted(string questId) => Of(questId).Completed = true;

    public void OnFailed(string questId) => Of(questId).Failed = true;

    /// <summary>An objective became live. Optional activations are silent: they appear in the journal, and
    /// announcing a side step the moment it unlocks competes with the step that matters.</summary>
    public void OnObjectiveActivated(string questId, int index, bool optional)
    {
        if (optional)
        {
            return;
        }

        Pending p = Of(questId);
        if (p.Activated < 0)
        {
            p.Activated = index;
        }
    }

    public void OnObjectiveCompleted(string questId, int index, bool optional)
    {
        Pending p = Of(questId);
        if (optional)
        {
            if (!p.OptionalDone.Contains(index))
            {
                p.OptionalDone.Add(index);
            }

            return;
        }

        p.RequiredDone = index;
    }

    /// <summary>Returns the toasts for everything fed since the last flush, oldest quest first, and clears.</summary>
    public List<QuestNoticeIntent> Flush()
    {
        var intents = new List<QuestNoticeIntent>();
        foreach (string questId in _order)
        {
            Pending p = _pending[questId];
            if (p.Completed)
            {
                intents.Add(new QuestNoticeIntent(QuestNoticeKind.Completed, questId, -1, -1));
                continue;
            }

            if (p.Failed)
            {
                intents.Add(new QuestNoticeIntent(QuestNoticeKind.Failed, questId, -1, -1));
                continue;
            }

            if (p.Started)
            {
                intents.Add(new QuestNoticeIntent(QuestNoticeKind.Started, questId, p.Activated, -1));
            }
            else if (p.Activated >= 0)
            {
                intents.Add(new QuestNoticeIntent(
                    QuestNoticeKind.NewObjective, questId, p.Activated, p.RequiredDone));
            }
            else if (p.RequiredDone >= 0)
            {
                intents.Add(new QuestNoticeIntent(QuestNoticeKind.Updated, questId, p.RequiredDone, p.RequiredDone));
            }

            foreach (int optional in p.OptionalDone)
            {
                intents.Add(new QuestNoticeIntent(QuestNoticeKind.OptionalDone, questId, optional, -1));
            }
        }

        _pending.Clear();
        _order.Clear();
        return intents;
    }

    private Pending Of(string questId)
    {
        if (!_pending.TryGetValue(questId, out Pending? p))
        {
            p = new Pending();
            _pending[questId] = p;
            _order.Add(questId);
        }

        return p;
    }
}

/// <summary>Which cue a quest toast plays.</summary>
public static class QuestNoticeCues
{
    public const string Started = "ui.quest.started";
    public const string Updated = "ui.quest.updated";
    public const string Completed = "ui.quest.completed";
    public const string ChapterTitle = "ui.chapter.title";
    public const string OptionalDone = "ui.objective.optional";

    public static string? For(QuestNoticeKind kind) => kind switch
    {
        QuestNoticeKind.Started => Started,
        QuestNoticeKind.NewObjective or QuestNoticeKind.Updated => Updated,
        QuestNoticeKind.Completed => Completed,
        QuestNoticeKind.OptionalDone => OptionalDone,
        _ => null,
    };
}
