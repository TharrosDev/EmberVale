using System.IO;
using System.Text;
using System.Text.Json;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Save;
using Embervale.World;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Embervale.Analytics;

/// <summary>
/// Dev-only telemetry sink. Subscribes to the EventBus and appends a JSON-lines log, one file per
/// session and one JSON object per line, that <c>python tools/analytics.py summary</c> reads.
///
/// <para><b>Where.</b> <c>&lt;EMBERVALE_ARTIFACTS&gt;/analytics/</c> when that variable is set (every
/// SDK run), otherwise the <c>analytics</c> folder of the user directory, which honours
/// <c>EMBERVALE_USER_DIR</c>, so an automated run no longer writes into the player's own folder. The
/// file is <c>session_&lt;unix ms&gt;_&lt;pid&gt;_&lt;n&gt;.jsonl</c> and is only created by the first
/// row, so a session that recorded nothing leaves nothing.</para>
///
/// <para><b>Rows.</b> Every row has <c>type</c>, <c>t</c> (wall-clock unix seconds), <c>pt</c>
/// (play seconds this session, which stop while the tree is paused) and <c>region</c> once known.
/// <c>session_start</c> (arguments, headless, automation, slot) is first. Then <c>death</c>
/// (<c>player</c> true for the player's own), <c>quest_start</c>, <c>quest_complete</c> and
/// <c>quest_fail</c> (with <c>seconds</c> of play since the start), <c>level_up</c>,
/// <c>gold</c> (<c>delta</c>, <c>total</c>, <c>source</c>), <c>region_transition</c>,
/// <c>fast_travel</c>, <c>corruption_tier</c>, <c>save</c> and <c>event</c> (an
/// <see cref="AnalyticsEvent"/>). <c>session_end</c> is last and carries the session's totals
/// (<see cref="AnalyticsAggregate"/>), damage by source and target among them: a hit is counted,
/// never written.</para>
///
/// <para><b>Gold sources</b> are inferred, not declared: <c>pickup</c> when gold was picked up in
/// the same frame, <c>quest</c> when a quest completed in it, <c>menu</c> when it changed while the
/// tree was paused (a shop, a craft), otherwise <c>other</c> (a dev command, a dialogue reward).</para>
///
/// <para>It also feeds the <see cref="FlightRecorder"/> and dumps it, once per session, to
/// <c>flight_&lt;stamp&gt;.jsonl</c> beside the log when the first invariant breaks.</para>
///
/// Gated on <see cref="OS.IsDebugBuild"/>: in a retail/exported build it subscribes to nothing,
/// opens no file, and is a complete no-op. It writes a log (not gameplay state), so it is
/// deliberately NOT <c>ISaveable</c> — nothing here round-trips through save/load.
/// </summary>
[GlobalClass]
public partial class AnalyticsSink : Node
{
    private const string Folder = "analytics";

    private static int _sessionsThisProcess;

    private readonly AnalyticsAggregate _totals = new();
    private FileAccess? _file;
    private bool _active;
    private bool _openFailed;
    private string _directory = string.Empty;
    private string _stamp = string.Empty;
    private string _region = string.Empty;
    private double _playSeconds;

    // Gold is watched through the player's inventory; see the class summary for the sources.
    private bool _goldPrimed;
    private int _gold;
    private int _pendingGold;
    private bool _sawPickup;
    private bool _sawQuest;
    private bool _sawMenu;
    private bool _flightDumped;

    /// <summary>The live session's totals, for a dev command or a harness.</summary>
    public AnalyticsAggregate Totals => _totals;

    /// <summary>Where this session's files go: the artifacts directory of an automated run, else
    /// the (possibly isolated) user directory.</summary>
    public static string ResolveDirectory()
    {
        string artifacts = OS.GetEnvironment("EMBERVALE_ARTIFACTS");
        return string.IsNullOrWhiteSpace(artifacts)
            ? UserDataPaths.Resolve(Folder)
            : artifacts.Replace('\\', '/').TrimEnd('/') + "/" + Folder;
    }

    public override void _Ready()
    {
        // Retail builds get a true no-op: no subscriptions, no file, no overhead.
        if (!OS.IsDebugBuild())
        {
            SetProcess(false);
            return;
        }

        _active = true;
        _directory = ResolveDirectory();
        int ordinal = System.Threading.Interlocked.Increment(ref _sessionsThisProcess);
        _stamp = $"{(long)(Time.GetUnixTimeFromSystem() * 1000d)}_{System.Environment.ProcessId}_{ordinal}";

        EventBus bus = EventBus.Instance;
        bus.Subscribe<AnalyticsEvent>(OnAnalytics);
        bus.Subscribe<EntityDiedEvent>(OnDeath);
        bus.Subscribe<DamageDealtEvent>(OnDamage);
        bus.Subscribe<QuestStartedEvent>(OnQuestStarted);
        bus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Subscribe<QuestFailedEvent>(OnQuestFailed);
        bus.Subscribe<LeveledUpEvent>(OnLevelUp);
        bus.Subscribe<XpGainedEvent>(OnXp);
        bus.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        bus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        // Stage-A actions (Phase 25.5F): region travel, fast travel, corruption-tier shifts, saves.
        bus.Subscribe<RegionChangedEvent>(OnRegionTransition);
        bus.Subscribe<FastTravelRequestedEvent>(OnFastTravel);
        bus.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTier);
        bus.Subscribe<GameSavedEvent>(OnGameSaved);
        Log.Written += OnLogWritten;
        Invariant.Violated += OnInvariantViolated;
    }

    public override void _ExitTree()
    {
        if (!_active)
        {
            return;
        }

        Log.Written -= OnLogWritten;
        Invariant.Violated -= OnInvariantViolated;
        if (EventBus.Instance is { } bus)
        {
            bus.Unsubscribe<AnalyticsEvent>(OnAnalytics);
            bus.Unsubscribe<EntityDiedEvent>(OnDeath);
            bus.Unsubscribe<DamageDealtEvent>(OnDamage);
            bus.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
            bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            bus.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
            bus.Unsubscribe<LeveledUpEvent>(OnLevelUp);
            bus.Unsubscribe<XpGainedEvent>(OnXp);
            bus.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
            bus.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
            bus.Unsubscribe<RegionChangedEvent>(OnRegionTransition);
            bus.Unsubscribe<FastTravelRequestedEvent>(OnFastTravel);
            bus.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTier);
            bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        }

        FlushGold();
        WriteSessionEnd();
        _file?.Close();
        _file = null;
        _active = false;
    }

    /// <summary>Play time, and the once-a-frame flush of gold changes. Not pause-immune on purpose:
    /// seconds spent in a menu are not seconds spent on a quest.</summary>
    public override void _Process(double delta)
    {
        _playSeconds += delta;
        if (!_goldPrimed)
        {
            // The baseline is taken on the first frame, after a load has finished restoring the
            // pack: restored gold is not income.
            if (PlayerInventory() is { } inventory)
            {
                _gold = inventory.CountOf(GameIds.Currency.Gold);
                _goldPrimed = true;
            }

            return;
        }

        FlushGold();
    }

    // --- rows ------------------------------------------------------------------------------------

    private void OnAnalytics(AnalyticsEvent e) =>
        Record("event", new Godot.Collections.Dictionary { { "name", e.Name }, { "detail", e.Detail } });

    private void OnDeath(EntityDiedEvent e)
    {
        string entity = Label(e.Entity);
        string killer = e.Killer == null ? string.Empty : Label(e.Killer);
        _totals.Death(entity, killer);
        var fields = new Godot.Collections.Dictionary { { "entity", entity }, { "killer", killer } };
        if (entity == AnalyticsAggregate.Player)
        {
            fields["player"] = true;
        }

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

    /// <summary>Counted, never written: a fight is hundreds of these. Only a hit on or by the player
    /// reaches the flight recorder.</summary>
    private void OnDamage(DamageDealtEvent e)
    {
        string source = e.Source == null ? "world" : Label(e.Source);
        string target = Label(e.Target);
        _totals.Damage(source, target, e.Amount);
        if (source == AnalyticsAggregate.Player || target == AnalyticsAggregate.Player)
        {
            FlightRecorder.Shared.Note("hit", $"{source} > {target} {e.Amount:0} {e.Type}");
        }
    }

    private void OnQuestStarted(QuestStartedEvent e)
    {
        _totals.QuestStarted(e.Quest.Id, _playSeconds);
        Record("quest_start", new Godot.Collections.Dictionary { { "quest", e.Quest.Id } });
    }

    private void OnQuestCompleted(QuestCompletedEvent e)
    {
        _sawQuest = true;
        RecordQuestEnd("quest_complete", "complete", e.Quest.Id);
    }

    private void OnQuestFailed(QuestFailedEvent e) => RecordQuestEnd("quest_fail", "failed", e.Quest.Id);

    private void RecordQuestEnd(string type, string result, string quest)
    {
        var fields = new Godot.Collections.Dictionary { { "quest", quest } };
        if (_totals.QuestEnded(quest, _playSeconds, result) is { } seconds)
        {
            fields["seconds"] = System.Math.Round(seconds, 1);
        }

        Record(type, fields);
    }

    private void OnLevelUp(LeveledUpEvent e) =>
        Record("level_up", new Godot.Collections.Dictionary { { "level", e.NewLevel } });

    private void OnXp(XpGainedEvent e)
    {
        if (e.Entity is PlayerCharacter)
        {
            _totals.AddXp(e.Amount);
        }
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (e.Owner is PlayerCharacter && e.Item.Id == GameIds.Currency.Gold)
        {
            _sawPickup = true;
        }
    }

    private void OnInventoryChanged(InventoryChangedEvent e)
    {
        if (!_goldPrimed || e.Owner is not PlayerCharacter player ||
            player.GetComponent<InventoryComponent>() is not { } inventory)
        {
            return;
        }

        int now = inventory.CountOf(GameIds.Currency.Gold);
        if (now == _gold)
        {
            return;
        }

        _pendingGold += now - _gold;
        _gold = now;
        if (IsInsideTree() && GetTree().Paused)
        {
            _sawMenu = true;
        }
    }

    private void FlushGold()
    {
        int delta = _pendingGold;
        string source = _sawPickup ? "pickup" : _sawQuest ? "quest" : _sawMenu ? "menu" : "other";
        _pendingGold = 0;
        _sawPickup = false;
        _sawQuest = false;
        _sawMenu = false;
        if (delta == 0)
        {
            return;
        }

        _totals.Gold(delta, source);
        Record("gold", new Godot.Collections.Dictionary { { "delta", delta }, { "total", _gold }, { "source", source } });
    }

    private void OnRegionTransition(RegionChangedEvent e)
    {
        _region = e.RegionId;
        Record("region_transition", new Godot.Collections.Dictionary { { "from", e.FromRegionId } });
    }

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

    // --- flight recorder -------------------------------------------------------------------------

    /// <summary>May run on a worker thread: it only notes.</summary>
    private static void OnLogWritten(Log.Level level, string message) =>
        FlightRecorder.Shared.Note(level == Log.Level.Error ? "error" : "warn", message);

    private void OnInvariantViolated(string message)
    {
        if (_flightDumped)
        {
            return;
        }

        _flightDumped = true;
        string path = ProjectSettings.GlobalizePath($"{_directory}/flight_{_stamp}.jsonl");

        // Info, not a warning: the violation is already an error in the log, and a handler of
        // Log.Written must not log through it.
        Log.Info(FlightRecorder.Shared.Dump(path, message)
            ? $"Flight recorder: the events leading up to the first invariant violation are in {path}."
            : $"Flight recorder: could not write {path}.");
    }

    // --- writing ---------------------------------------------------------------------------------

    private static InventoryComponent? PlayerInventory() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<InventoryComponent>()
            : null;

    private static string Label(IEntity entity) =>
        entity is PlayerCharacter ? AnalyticsAggregate.Player :
        string.IsNullOrEmpty(entity.TemplateId) ? entity.DisplayName : entity.TemplateId;

    /// <summary>Opens the session file on the first row and writes the header row.</summary>
    private bool EnsureOpen()
    {
        if (_file != null)
        {
            return true;
        }

        if (!_active || _openFailed)
        {
            return false;
        }

        DirAccess.MakeDirRecursiveAbsolute(_directory);
        string path = $"{_directory}/session_{_stamp}.jsonl";
        _file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (_file == null)
        {
            _openFailed = true;
            Log.Warn($"AnalyticsSink: could not open '{path}' ({FileAccess.GetOpenError()}); analytics disabled.");
            return false;
        }

        Log.Info($"AnalyticsSink (dev): logging telemetry to {ProjectSettings.GlobalizePath(path)}.");
        Write("session_start", new Godot.Collections.Dictionary
        {
            { "args", string.Join(' ', OS.GetCmdlineUserArgs()) },
            { "headless", DisplayServer.GetName() == "headless" },
            { "automation", !string.IsNullOrWhiteSpace(OS.GetEnvironment("EMBERVALE_USER_DIR")) },
            { "slot", SaveManager.Instance?.ActiveSlot ?? string.Empty },
        });
        return true;
    }

    private void Record(string type, Godot.Collections.Dictionary fields)
    {
        if (EnsureOpen())
        {
            Write(type, fields);
        }
    }

    private void Write(string type, Godot.Collections.Dictionary fields)
    {
        fields["t"] = Time.GetUnixTimeFromSystem();
        fields["pt"] = System.Math.Round(_playSeconds, 2);
        fields["type"] = type;
        if (_region.Length == 0 && ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer))
        {
            // No transition has been seen yet: the session began in this region.
            _region = streamer.ActiveRegionId;
        }

        if (_region.Length > 0)
        {
            fields["region"] = _region;
        }

        string line = Json.Stringify(fields);
        _file!.StoreLine(line);
        _file.Flush();
        FlightRecorder.Shared.Note("row", line);
    }

    /// <summary>The totals row. Written when the session recorded anything at all, so a reader can
    /// take the last line of a file as its summary.</summary>
    private void WriteSessionEnd()
    {
        if ((_file == null && _totals.IsEmpty) || !EnsureOpen())
        {
            return;
        }

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("type", "session_end");
            json.WriteNumber("t", Time.GetUnixTimeFromSystem());
            json.WriteNumber("pt", System.Math.Round(_playSeconds, 2));
            if (_region.Length > 0)
            {
                json.WriteString("region", _region);
            }

            _totals.WriteTo(json);
            json.WriteEndObject();
        }

        _file!.StoreLine(Encoding.UTF8.GetString(stream.ToArray()));
        _file.Flush();
    }
}
