using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Progression;
using Embervale.Save;
using Godot;

namespace Embervale.Quests;

/// <summary>
/// The actor's quest journal. It holds active and completed <see cref="QuestProgress"/>,
/// advances objectives by reacting to gameplay events (<see cref="EntityDiedEvent"/> for
/// kills it caused, <see cref="ItemPickedUpEvent"/> for collections), and on completion
/// grants rewards through the sibling <see cref="ProgressionComponent"/> (XP) and
/// <see cref="InventoryComponent"/> (gold + items). Persists the whole log via
/// <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class QuestLogComponent : EntityComponent, ISaveable
{
    /// <summary>
    /// How close the player must come for a <see cref="ObjectiveType.Reach"/> objective to count as
    /// arrived.
    ///
    /// ⚠️ <b>Deliberately its own number rather than <c>MapService.DiscoveryRadius</c>, which is also
    /// 20 m today.</b> Spotting a place and arriving at it are different questions — a landmark can
    /// be visible from much further than a quest should accept as "you are there" — so the two want
    /// to be tunable apart. Sharing the constant would silently couple them the first time either is
    /// changed, which is the kind of link nobody finds by reading one file.
    /// </summary>
    public const float ArrivalRadius = 12f;

    /// <summary>Reach poll cadence, matching <c>MapService</c>'s own 4 Hz tick.</summary>
    private const float ReachTickSeconds = 0.25f;

    private readonly Dictionary<string, QuestProgress> _quests = new();

    /// <summary>Scratch buffer for the Reach poll, reused so a 4 Hz tick allocates nothing.</summary>
    private readonly List<string> _reachTargets = new();

    private float _sinceReachTick;

    private ProgressionComponent? _progression;
    private InventoryComponent? _inventory;

    public string SaveId => SaveKey("questlog");

    public IReadOnlyCollection<QuestProgress> Quests => _quests.Values;

    /// <summary>
    /// The quest the player has asked the HUD to follow, or empty for "whichever is first".
    ///
    /// Set through <see cref="Track"/> rather than directly, so an id that is not in the log (a stale
    /// save, a quest completed since) can never be stored.
    /// </summary>
    public string TrackedQuestId { get; private set; } = string.Empty;

    /// <summary>
    /// The quest the HUD tracker and the compass follow.
    ///
    /// ⚠️ <b>This is the single answer to "which quest am I on", and before 39.5B there were two.</b>
    /// <c>GameHud.UpdateQuest</c> and <c>CompassStrip.ResolveObjectiveTarget</c> each scanned for the
    /// first Active quest independently — which happened to agree only because both walked the same
    /// dictionary in the same order, and would have silently diverged the first time either gained a
    /// filter. The tracker panel and the compass marker pointing at different quests is the kind of
    /// defect a player reads as the compass being wrong.
    ///
    /// Falls back to the first active quest when nothing is explicitly tracked, which is exactly the
    /// old behaviour — so a save from before this existed, and a player who never touches the
    /// journal, both get what they got yesterday.
    /// </summary>
    public QuestProgress? Tracked
    {
        get
        {
            if (TrackedQuestId.Length > 0 &&
                _quests.TryGetValue(TrackedQuestId, out QuestProgress? chosen) &&
                chosen.Status == QuestStatus.Active)
            {
                return chosen;
            }

            foreach (QuestProgress progress in _quests.Values)
            {
                if (progress.Status == QuestStatus.Active)
                {
                    return progress;
                }
            }

            return null;
        }
    }

    /// <summary>Follows <paramref name="questId"/> on the HUD, or clears the choice when it is empty
    /// or names a quest that is not active. Idempotent; safe to call with anything.</summary>
    public void Track(string? questId)
    {
        TrackedQuestId = !string.IsNullOrEmpty(questId) && IsActive(questId) ? questId : string.Empty;
    }

    protected override void OnInitialize()
    {
        _progression = Entity!.GetComponent<ProgressionComponent>();
        _inventory = Entity.GetComponent<InventoryComponent>();

        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
        SaveManager.Instance?.Unregister(this);
    }

    public bool IsActive(string questId) =>
        _quests.TryGetValue(questId, out QuestProgress? p) && p.Status == QuestStatus.Active;

    public bool IsCompleted(string questId) =>
        _quests.TryGetValue(questId, out QuestProgress? p) && p.Status == QuestStatus.Completed;

    public bool HasQuest(string questId) => _quests.ContainsKey(questId);

    /// <summary>True if the quest isn't already in the log and its prerequisite (if any)
    /// has been completed.</summary>
    public bool CanStart(QuestResource quest)
    {
        if (quest == null || _quests.ContainsKey(quest.Id))
        {
            return false;
        }

        return string.IsNullOrEmpty(quest.PrerequisiteQuestId) || IsCompleted(quest.PrerequisiteQuestId);
    }

    /// <summary>Adds a quest to the log as Active. Returns false if it can't be started.</summary>
    public bool StartQuest(QuestResource quest)
    {
        if (!CanStart(quest))
        {
            return false;
        }

        var progress = new QuestProgress(quest);
        _quests[quest.Id] = progress;
        Log.Info($"Quest started: {quest.Title}");
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new QuestStartedEvent(Entity, quest));
        }

        // A quest with no objectives (or all already satisfied) completes immediately.
        TryComplete(progress);
        return true;
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (Entity == null || e.Killer == null || !ReferenceEquals(e.Killer, Entity))
        {
            return;
        }

        Advance(ObjectiveType.Kill, e.Entity.TemplateId);
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (Entity == null || !ReferenceEquals(e.Owner, Entity))
        {
            return;
        }

        Advance(ObjectiveType.Collect, e.Item.Id, e.Quantity);
    }

    /// <summary>Advances Talk objectives when this actor finishes a conversation (41A). Fires on
    /// <em>ended</em> rather than started: the objective is having the conversation, not opening the
    /// panel, and a player who dismisses a graph on the first line has still met the speaker.</summary>
    private void OnDialogueEnded(DialogueEndedEvent e)
    {
        if (Entity == null || !ReferenceEquals(e.Player, Entity) || e.Dialogue == null)
        {
            return;
        }

        Advance(ObjectiveType.Talk, e.Dialogue.Id);
    }

    /// <summary>
    /// Polls arrival for active <see cref="ObjectiveType.Reach"/> objectives (41A).
    ///
    /// ⚠️ <b>This is a distance test and NOT a discovery check</b>, which is the one thing about
    /// Reach that is easy to get wrong — see <see cref="ObjectiveType.Reach"/>. It also lives here
    /// rather than in <c>MapService</c> on purpose: <c>src/World</c> has no business knowing what a
    /// quest is, and the map would have to poll all 64 locations forever to answer a question that is
    /// usually about none of them.
    ///
    /// ponytail: the scan below runs at 4 Hz and early-outs on the common case (no Reach objective
    /// active) before touching the service locator or the player transform. Objectives are a handful
    /// per save; index them only if a profile ever says so.
    /// </summary>
    public override void _Process(double delta)
    {
        _sinceReachTick += (float)delta;
        if (_sinceReachTick < ReachTickSeconds)
        {
            return;
        }

        _sinceReachTick = 0f;
        CheckReachObjectives();
    }

    private void CheckReachObjectives()
    {
        if (Entity == null || _quests.Count == 0)
        {
            return;
        }

        // Collected first so the Advance calls below cannot mutate the log while it is being walked
        // — completing one quest can start another through its rewards, the same reason Advance
        // snapshots. Reused across ticks to keep a 4 Hz poll from allocating.
        _reachTargets.Clear();
        foreach (QuestProgress progress in _quests.Values)
        {
            if (progress.Status != QuestStatus.Active)
            {
                continue;
            }

            List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveResource objective = objectives[i];
                if (objective.Type == ObjectiveType.Reach &&
                    !progress.IsObjectiveComplete(i) &&
                    objective.TargetId.Length > 0)
                {
                    _reachTargets.Add(objective.TargetId);
                }
            }
        }

        if (_reachTargets.Count == 0 ||
            ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out World.MapService map))
        {
            return;
        }

        Vector3 here = Entity.Body.GlobalPosition;
        foreach (string locationId in _reachTargets)
        {
            if (map.PositionOf(locationId) is not { } target)
            {
                // The location's cell is not resident and no save remembered it, so there is nothing
                // to measure against. Silent by design: the player simply has not arrived yet.
                continue;
            }

            // Planar, matching MapService.TryDiscover — a marker on an upper floor is not further
            // away for being above you.
            float dx = target.X - here.X;
            float dz = target.Z - here.Z;
            if ((dx * dx) + (dz * dz) <= ArrivalRadius * ArrivalRadius)
            {
                Advance(ObjectiveType.Reach, locationId);
            }
        }
    }

    /// <summary>Advances every active objective matching the type+target by <paramref name="amount"/>.</summary>
    private void Advance(ObjectiveType type, string targetId, int amount = 1)
    {
        if (string.IsNullOrEmpty(targetId) || amount <= 0)
        {
            return;
        }

        // Snapshot: completion could mutate the log via rewards/chaining.
        var active = new List<QuestProgress>();
        foreach (QuestProgress p in _quests.Values)
        {
            if (p.Status == QuestStatus.Active)
            {
                active.Add(p);
            }
        }

        foreach (QuestProgress progress in active)
        {
            List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
            bool changed = false;

            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveResource objective = objectives[i];
                if (objective.Type != type || objective.TargetId != targetId || progress.IsObjectiveComplete(i))
                {
                    continue;
                }

                progress.Counts[i] = Mathf.Min(progress.Counts[i] + amount, objective.RequiredCount);
                changed = true;
                if (Entity != null)
                {
                    EventBus.Instance?.Publish(new QuestObjectiveAdvancedEvent(
                        Entity, progress.Quest, i, progress.Counts[i], objective.RequiredCount));
                }
            }

            if (changed)
            {
                TryComplete(progress);
            }
        }
    }

    private void TryComplete(QuestProgress progress)
    {
        if (progress.Status != QuestStatus.Active || !progress.AllObjectivesMet())
        {
            return;
        }

        progress.Status = QuestStatus.Completed;
        GrantRewards(progress.Quest);
        Log.Info($"Quest completed: {progress.Quest.Title}");
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new QuestCompletedEvent(Entity, progress.Quest));
        }
    }

    private void GrantRewards(QuestResource quest)
    {
        if (quest.XpReward > 0)
        {
            _progression?.AddXp(quest.XpReward);
        }

        // Before the no-inventory bail below: standing is owed whether or not the actor can carry
        // anything. Resolved lazily rather than cached, since only the player has one.
        if (!string.IsNullOrEmpty(quest.FactionRewardId) && quest.FactionRewardAmount != 0)
        {
            Entity?.GetComponent<ReputationComponent>()?.Add(quest.FactionRewardId, quest.FactionRewardAmount);
        }

        if (quest.GoldReward > 0 && _inventory != null && ItemDatabase.Get(quest.GoldItemId) is { } gold)
        {
            _inventory.AddItem(gold, quest.GoldReward);
        }

        if (_inventory == null)
        {
            return;
        }

        foreach (Variant element in quest.RewardItems)
        {
            if (element.As<QuestItemReward>() is not { } reward || reward.Quantity <= 0)
            {
                continue;
            }

            if (ItemDatabase.Get(reward.ItemId) is { } item)
            {
                _inventory.AddItem(item, reward.Quantity);
            }
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var quests = new Godot.Collections.Array();
        foreach (QuestProgress progress in _quests.Values)
        {
            quests.Add(progress.Save());
        }

        return new Godot.Collections.Dictionary
        {
            ["quests"] = quests,
            ["tracked"] = TrackedQuestId,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _quests.Clear();

        // ⚠️ Cleared unconditionally BEFORE the restore, not merged over (CLAUDE.md §7). A quickload
        // keeps every live component, so without this line a quest tracked in the timeline being
        // abandoned stays tracked in the one being restored — and if that quest is not in the save,
        // the fallback below quietly hands the HUD a different quest than the journal shows.
        TrackedQuestId = string.Empty;

        if (data.TryGetValue("quests", out Variant questsVar))
        {
            foreach (Variant entry in questsVar.AsGodotArray())
            {
                QuestProgress? progress = QuestProgress.FromSave(entry.AsGodotDictionary());
                if (progress != null)
                {
                    _quests[progress.Quest.Id] = progress;
                }
            }
        }

        // Re-validated through Track, so a saved id whose quest has since been removed from the
        // database, or completed, resolves to "no explicit choice" rather than to nothing at all.
        if (data.TryGetValue("tracked", out Variant trackedVar))
        {
            Track(trackedVar.AsString());
        }

        // The quest-log UI rebuilds from this component on GameLoadedEvent.
    }
}
