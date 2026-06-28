using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Analytics;

/// <summary>
/// Dev-only telemetry sink. Subscribes to the EventBus and appends a structured JSON-lines log to
/// <c>user://analytics/</c> so balance/QA later have data — deaths by location, quest funnels,
/// level-ups — plus any <see cref="AnalyticsEvent"/> a system explicitly publishes. One file per
/// session (<c>session_&lt;unixtime&gt;.jsonl</c>), one JSON object per line.
///
/// Gated on <see cref="OS.IsDebugBuild"/>: in a retail/exported build it subscribes to nothing,
/// opens no file, and is a complete no-op. It writes a log (not gameplay state), so it is
/// deliberately NOT <c>ISaveable</c> — nothing here round-trips through save/load.
/// </summary>
[GlobalClass]
public partial class AnalyticsSink : Node
{
    private const string Directory = "user://analytics";

    private FileAccess? _file;
    private bool _active;

    public override void _Ready()
    {
        // Retail builds get a true no-op: no subscriptions, no file, no overhead.
        if (!OS.IsDebugBuild() || !OpenSessionFile())
        {
            return;
        }

        _active = true;
        EventBus bus = EventBus.Instance;
        bus.Subscribe<AnalyticsEvent>(OnAnalytics);
        bus.Subscribe<EntityDiedEvent>(OnDeath);
        bus.Subscribe<QuestStartedEvent>(OnQuestStarted);
        bus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Subscribe<LeveledUpEvent>(OnLevelUp);
        // Stage-A actions (Phase 25.5F): region travel, fast travel, corruption-tier shifts, saves.
        bus.Subscribe<RegionTransitionRequestedEvent>(OnRegionTransition);
        bus.Subscribe<FastTravelRequestedEvent>(OnFastTravel);
        bus.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTier);
        bus.Subscribe<GameSavedEvent>(OnGameSaved);
    }

    public override void _ExitTree()
    {
        if (!_active)
        {
            return;
        }

        if (EventBus.Instance is { } bus)
        {
            bus.Unsubscribe<AnalyticsEvent>(OnAnalytics);
            bus.Unsubscribe<EntityDiedEvent>(OnDeath);
            bus.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
            bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            bus.Unsubscribe<LeveledUpEvent>(OnLevelUp);
            bus.Unsubscribe<RegionTransitionRequestedEvent>(OnRegionTransition);
            bus.Unsubscribe<FastTravelRequestedEvent>(OnFastTravel);
            bus.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTier);
            bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        }

        _file?.Close();
        _file = null;
        _active = false;
    }

    private bool OpenSessionFile()
    {
        DirAccess.MakeDirRecursiveAbsolute(Directory);
        string path = $"{Directory}/session_{(long)Time.GetUnixTimeFromSystem()}.jsonl";
        _file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (_file == null)
        {
            Log.Warn($"AnalyticsSink: could not open '{path}' ({FileAccess.GetOpenError()}); analytics disabled.");
            return false;
        }

        Log.Info($"AnalyticsSink (dev): logging telemetry to {path}.");
        return true;
    }

    private void OnAnalytics(AnalyticsEvent e) =>
        Record("event", new Godot.Collections.Dictionary { { "name", e.Name }, { "detail", e.Detail } });

    private void OnDeath(EntityDiedEvent e)
    {
        var fields = new Godot.Collections.Dictionary
        {
            { "entity", Label(e.Entity) },
            { "killer", e.Killer == null ? string.Empty : Label(e.Killer) },
        };

        // "Deaths by location": capture where it happened while the body is still valid.
        if (e.Entity.Body is { } body && GodotObject.IsInstanceValid(body))
        {
            Vector3 p = body.GlobalPosition;
            fields["x"] = p.X;
            fields["y"] = p.Y;
            fields["z"] = p.Z;
        }

        Record("death", fields);
    }

    private void OnQuestStarted(QuestStartedEvent e) =>
        Record("quest_start", new Godot.Collections.Dictionary { { "quest", e.Quest.Id } });

    private void OnQuestCompleted(QuestCompletedEvent e) =>
        Record("quest_complete", new Godot.Collections.Dictionary { { "quest", e.Quest.Id } });

    private void OnLevelUp(LeveledUpEvent e) =>
        Record("level_up", new Godot.Collections.Dictionary { { "level", e.NewLevel } });

    private void OnRegionTransition(RegionTransitionRequestedEvent e) =>
        Record("region_transition", new Godot.Collections.Dictionary { { "region", e.RegionId } });

    private void OnFastTravel(FastTravelRequestedEvent e) =>
        Record("fast_travel", new Godot.Collections.Dictionary { { "node", e.NodeId } });

    private void OnCorruptionTier(CorruptionTierChangedEvent e) =>
        Record("corruption_tier", new Godot.Collections.Dictionary
        {
            { "from", e.Previous.ToString() },
            { "to", e.Current.ToString() },
        });

    private void OnGameSaved(GameSavedEvent e) =>
        Record("save", new Godot.Collections.Dictionary { { "slot", e.Slot }, { "autosave", e.IsAutosave } });

    private static string Label(IEntity entity) =>
        string.IsNullOrEmpty(entity.TemplateId) ? entity.DisplayName : entity.TemplateId;

    private void Record(string type, Godot.Collections.Dictionary fields)
    {
        if (_file == null)
        {
            return;
        }

        fields["t"] = Time.GetUnixTimeFromSystem();
        fields["type"] = type;
        _file.StoreLine(Json.Stringify(fields));
        _file.Flush();
    }
}
