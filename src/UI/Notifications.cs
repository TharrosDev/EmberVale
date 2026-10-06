using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Economy;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Movement;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Settings;
using Embervale.Shrines;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The toast/notification feed: a top-right stack of transient <see cref="Toast"/> plates
/// announcing discrete, meaningful moments — level-ups, quest start/completion, and world
/// events beginning/ending. Event-driven, so any system that raises one of these is surfaced
/// to the player without coupling. Built through <see cref="UiTheme"/>.
///
/// Four rules shape the feed, the arithmetic of each in <see cref="ToastRules"/>. A toast holds for
/// as long as its words take to read, times the player's toast-duration setting. A notice that
/// repeats one already waiting or already on screen adds to its count ("×3") instead of stacking.
/// While the player is fighting only warnings are shown; the rest wait and leave in order once the
/// fight is over. And with the Toasts HUD element hidden, only warnings are shown: a save that
/// failed or a full pack has no other way to reach the player.
/// </summary>
public partial class Notifications : CanvasLayer
{
    private const int MaxVisible = 3;
    private const int MaxQueued = 12;

    /// <summary>Height one toast with a caption line takes, used to decide how many fit between the tracker
    /// and the minimap. An estimate on purpose: it only has to be close enough that a stack never reaches
    /// the minimap, and a toast that is taller just means one fewer shows at a time.</summary>
    private const float ToastSlot = 72f;

    /// <summary>Wrap width of a toast's text; wide enough that a quest title fits in two lines, not four.</summary>
    private const float ToastTextWidth = 260f;

    private enum NoticeCategory { Minor, Reward, Quest, Warning, Major, Bark }

    private sealed class Notice
    {
        public required string Text { get; init; }
        public required Color Accent { get; init; }
        public required NoticeCategory Category { get; init; }

        /// <summary>A dim second line: the quest a step belongs to, the speaker of a bark.</summary>
        public string? Secondary { get; init; }

        /// <summary>The audio cue played when the toast is shown (not when it is queued).</summary>
        public string? Cue { get; init; }

        /// <summary>What makes two notices the same notice: one adds to the other's count.</summary>
        public required string Key { get; init; }

        /// <summary>Whether the toast names the journal's key: it is about a quest.</summary>
        public bool Journal { get; init; }

        public int Count { get; set; } = 1;
    }

    private VBoxContainer _stack = null!;
    private GameHud? _hud;
    private readonly ToastQueue<Notice> _queue = new();
    private readonly Dictionary<string, Notice> _coalesced = new();
    private readonly Dictionary<string, Toast> _shown = new();
    private int _visible;

    // The last blow traded with something, and everyone blows were traded with since the fight
    // began: what "in a fight" means to the feed.
    private double _blowAt = double.NegativeInfinity;
    private readonly HashSet<StatsComponent> _opponents = new();
    private static readonly System.Predicate<StatsComponent> Fallen =
        static stats => !IsInstanceValid(stats) || !stats.IsAlive;

    /// <summary>Notices waiting to be shown. For harness validation.</summary>
    public int QueuedForCapture => _queue.Count;

    /// <summary>The largest count on a toast on screen (1 with none collapsed, 0 with no toast).
    /// For harness validation.</summary>
    public int ToastCountForCapture
    {
        get
        {
            int most = 0;
            foreach (Toast toast in _shown.Values)
            {
                most = Mathf.Max(most, IsInstanceValid(toast) ? toast.Count : 0);
            }

            return most;
        }
    }

    /// <summary>Ends the feed's combat reading at once, so a harness need not wait the fight out.</summary>
    public void EndCombatForCapture() => EndCombat();

    // One player action publishes several quest events in one frame; they are collected here and turned into
    // the toasts worth showing once per frame (QuestNoticeCoalescer).
    private readonly QuestNoticeCoalescer _questNotices = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Top-right, below the quest tracker — the top-centre column belongs to the boss bar /
        // event banner / nameplate stack (30.5B), which toasts used to overlap.
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _stack.AnchorLeft = 1f;
        _stack.AnchorRight = 1f;
        _stack.AnchorTop = 0f;
        _stack.AnchorBottom = 0f;
        _stack.GrowHorizontal = Control.GrowDirection.Begin;
        _stack.GrowVertical = Control.GrowDirection.End;
        _stack.OffsetLeft = -UiTheme.SpaceLg;
        _stack.OffsetRight = -UiTheme.SpaceLg;
        _stack.OffsetTop = UiTheme.SpaceLg;
        _stack.OffsetBottom = UiTheme.SpaceLg;
        AddChild(_stack);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        bus?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        bus?.Subscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        bus?.Subscribe<QuestStageChangedEvent>(OnStageChanged);
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<QuestFailedEvent>(OnQuestFailed);
        bus?.Subscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus?.Subscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus?.Subscribe<LocationDiscoveredEvent>(OnLocationDiscovered);
        bus?.Subscribe<GameSavedEvent>(OnGameSaved);
        SubscribeSave(bus);
        bus?.Subscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus?.Subscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus?.Subscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus?.Subscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
        bus?.Subscribe<WagerSettledEvent>(OnWagerSettled);
        bus?.Subscribe<SupplyShockRelievedEvent>(OnShockRelieved);
        SubscribeLoot(bus); // ics:inv-ui: pickups, pack full, recipe learned (Notifications.Loot.cs)
        bus?.Subscribe<MountChangedEvent>(OnMountChanged);
        bus?.Subscribe<MountRefusedEvent>(OnMountRefused);
        bus?.Subscribe<BlessingClaimedEvent>(OnBlessingClaimed);
        bus?.Subscribe<ShrineAlreadyVisitedEvent>(OnShrineAlreadyVisited);
        bus?.Subscribe<ShrineRefusedEvent>(OnShrineRefused);
        bus?.Subscribe<WorldHazardNoticeEvent>(OnWorldHazard);
        SubscribeMagic(bus);

        bus?.Subscribe<CompanionBarkEvent>(OnCompanionBark);
        bus?.Subscribe<Narrative.StoryToastRequestedEvent>(OnStoryToast);
        bus?.Subscribe<Combat.HitConfirmedEvent>(OnBlow);
        bus?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    /// <summary>A blow traded with something that fights back, or is being fought. A blow with no
    /// one behind it (a fall, a hazard) is not a fight.</summary>
    private void OnBlow(Combat.HitConfirmedEvent e)
    {
        Entities.IEntity? other = e.OnPlayer ? e.Source : e.ByPlayer ? e.Target : null;
        if (other != null && !Combat.CombatPerspective.IsPlayer(other) && other.GetComponent<StatsComponent>() is { } stats)
        {
            _opponents.Add(stats);
            _blowAt = Now();
        }
    }

    /// <summary>Whether the player is in a fight as far as the feed is concerned: a blow was traded a
    /// moment ago and one of those it was traded with is still standing. A fight ends at once with
    /// the last of them, not with the first.</summary>
    private bool InCombat()
    {
        if (HudDynamicRules.Lingering(Now(), _blowAt, HudDynamicRules.CombatLingerSeconds))
        {
            _opponents.RemoveWhere(Fallen);
            if (_opponents.Count > 0)
            {
                return true;
            }
        }

        EndCombat();
        return false;
    }

    private void EndCombat()
    {
        _opponents.Clear();
        _blowAt = double.NegativeInfinity;
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => RefreshGlyphs();

    private void OnBindingsChanged(InputBindingsChangedEvent e) => RefreshGlyphs();

    private void RefreshGlyphs()
    {
        foreach (Toast toast in _shown.Values)
        {
            if (IsInstanceValid(toast))
            {
                toast.RefreshGlyph();
            }
        }
    }

    public override void _Process(double delta)
    {
        bool protectedState = UiState.MenuOpen || GameManager.Instance?.State != GameState.Playing;

        // Nothing showing, nothing queued, nothing pending: the four passes below have no toast to
        // place, shed or present, and each of them walks the stack or asks the HUD for a layout rect.
        // Only the stack's visibility is kept current, so a toast pushed later appears into the
        // state it always did. (PresentQueued places the stack itself before it shows anything.)
        if (_visible == 0 && _queue.Count == 0 && !_questNotices.HasPending)
        {
            SetStackShown(!protectedState);
            return;
        }

        FlushQuestNotices();
        PlaceStack();
        ShedOverflow();
        SetStackShown(!protectedState);
        if (!protectedState)
        {
            PresentQueued();
        }
    }

    private int _stackShown = -1;

    private void SetStackShown(bool shown)
    {
        int key = shown ? 1 : 0;
        if (key != _stackShown)
        {
            _stackShown = key;
            _stack.Visible = shown;
        }
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        if (bus == null)
        {
            return;
        }

        bus.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        bus.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
        bus.Unsubscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        bus.Unsubscribe<QuestStageChangedEvent>(OnStageChanged);
        bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
        bus.Unsubscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus.Unsubscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus.Unsubscribe<LocationDiscoveredEvent>(OnLocationDiscovered);
        bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        UnsubscribeSave(bus);
        bus.Unsubscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus.Unsubscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus.Unsubscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus.Unsubscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus.Unsubscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
        bus.Unsubscribe<WagerSettledEvent>(OnWagerSettled);
        bus.Unsubscribe<SupplyShockRelievedEvent>(OnShockRelieved);
        UnsubscribeLoot(bus); // ics:inv-ui
        bus.Unsubscribe<MountChangedEvent>(OnMountChanged);
        bus.Unsubscribe<MountRefusedEvent>(OnMountRefused);
        bus.Unsubscribe<BlessingClaimedEvent>(OnBlessingClaimed);
        bus.Unsubscribe<ShrineAlreadyVisitedEvent>(OnShrineAlreadyVisited);
        bus.Unsubscribe<ShrineRefusedEvent>(OnShrineRefused);
        bus.Unsubscribe<WorldHazardNoticeEvent>(OnWorldHazard);
        UnsubscribeMagic(bus);
        bus.Unsubscribe<CompanionBarkEvent>(OnCompanionBark);
        bus.Unsubscribe<Narrative.StoryToastRequestedEvent>(OnStoryToast);
        bus.Unsubscribe<Combat.HitConfirmedEvent>(OnBlow);
        bus.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    private void OnLeveledUp(LeveledUpEvent e)
    {
        // Say what the level bought: a milestone level pays two points, an ordinary one pays one.
        string text = e.SkillPointsGained switch
        {
            <= 0 => Loc.TF("notify.levelup", e.NewLevel),
            1 => Loc.TF("notify.levelup_point", e.NewLevel, e.SkillPointsGained),
            _ => Loc.TF("notify.levelup_points", e.NewLevel, e.SkillPointsGained),
        };
        Push(text, UiTheme.Accent, NoticeCategory.Major);
    }

    // --- Quests ---------------------------------------------------------------------------
    //
    // Every quest event is FED to the coalescer and turned into toasts once per frame (FlushQuestNotices).
    // A single action publishes several events - the step advanced, the stage changed, the next step
    // activated, sometimes the quest completed - and announcing each would read as a bug. Quest.Title and
    // an objective's text are Loc keys (data-authored), resolved at display time.

    private void OnQuestStarted(QuestStartedEvent e) => _questNotices.OnStarted(e.Quest.Id);

    private void OnObjectiveActivated(QuestObjectiveActivatedEvent e) =>
        _questNotices.OnObjectiveActivated(e.QuestId, e.ObjectiveIndex, IsOptional(e.QuestId, e.ObjectiveIndex));

    /// <summary>Only the completing half of a stage change matters here: the activating half is the same
    /// moment as <see cref="QuestObjectiveActivatedEvent"/> and would double-count.</summary>
    private void OnStageChanged(QuestStageChangedEvent e)
    {
        if (e.Completed)
        {
            _questNotices.OnObjectiveCompleted(e.QuestId, e.ObjectiveIndex, IsOptional(e.QuestId, e.ObjectiveIndex));
        }
    }

    private void OnQuestCompleted(QuestCompletedEvent e) => _questNotices.OnCompleted(e.Quest.Id);

    /// <summary>A quest lost (41B). It shares the world event's failure colour rather than the
    /// companion-downed one: what the player needs told apart here is "this ended badly" from "this
    /// ended well", and the downed toast that caused an escort failure has already fired beside it.</summary>
    private void OnQuestFailed(QuestFailedEvent e) => _questNotices.OnFailed(e.Quest.Id);

    private void FlushQuestNotices()
    {
        if (!_questNotices.HasPending)
        {
            return;
        }

        foreach (QuestNoticeIntent intent in _questNotices.Flush())
        {
            if (FindQuest(intent.QuestId) is not { } quest)
            {
                continue;
            }

            List<ObjectiveResource> objectives = quest.ObjectiveList();
            string title = Loc.T(quest.Title);
            string? Describe(int index) =>
                index >= 0 && index < objectives.Count ? Loc.T(objectives[index].ShortLabel()) : null;
            Color tint = quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide;
            string? cue = QuestNoticeCues.For(intent.Kind);

            switch (intent.Kind)
            {
                case QuestNoticeKind.Completed:
                    Push(Loc.TF("notify.quest_complete", title), UiTheme.Good, NoticeCategory.Major, cue: cue, journal: true);
                    break;

                case QuestNoticeKind.Failed:
                    Push(Loc.TF("notify.quest_failed", title), UiTheme.Bad, NoticeCategory.Warning, journal: true);
                    break;

                case QuestNoticeKind.Started:
                    Push(
                        Loc.TF("notify.quest_started", title), UiTheme.Text, NoticeCategory.Quest,
                        secondary: Describe(intent.ObjectiveIndex) is { } first ? Loc.TF("questui.new_objective", first) : null,
                        cue: cue, journal: true);
                    break;

                case QuestNoticeKind.NewObjective:
                    Push(
                        Loc.TF("questui.new_objective", Describe(intent.ObjectiveIndex) ?? title), tint,
                        NoticeCategory.Quest, secondary: title, cue: cue, journal: true);
                    break;

                case QuestNoticeKind.Updated:
                    Push(
                        Loc.TF("questui.updated_toast", title), UiTheme.QuestComplete, NoticeCategory.Quest,
                        secondary: Describe(intent.CompletedIndex) is { } met ? Loc.TF("hud.quest.objective_done", met) : null,
                        cue: cue, journal: true);
                    break;

                case QuestNoticeKind.OptionalDone:
                    Push(
                        Loc.TF("questui.optional_done", Describe(intent.ObjectiveIndex) ?? title),
                        UiTheme.QuestComplete, NoticeCategory.Quest, secondary: title, cue: cue, journal: true);
                    break;
            }
        }
    }

    private static bool IsOptional(string questId, int index)
    {
        if (FindQuest(questId) is not { } quest)
        {
            return false;
        }

        List<ObjectiveResource> objectives = quest.ObjectiveList();
        return index >= 0 && index < objectives.Count && objectives[index].IsOptional;
    }

    /// <summary>The quest by id, preferring the player's own log (the instance the events describe, which
    /// also covers a quest that is not in the database) over the database.</summary>
    private static QuestResource? FindQuest(string questId)
    {
        if (Core.Services.ServiceLocator.Instance is { } locator &&
            locator.TryGet(out Player.PlayerCharacter player) &&
            player.GetComponent<QuestLogComponent>() is { } log)
        {
            foreach (QuestProgress progress in log.Quests)
            {
                if (progress.Quest.Id == questId)
                {
                    return progress.Quest;
                }
            }
        }

        return QuestDatabase.Get(questId);
    }

    private void OnCompanionBark(CompanionBarkEvent e) => PushBark(e.CompanionId, e.TextKey);

    /// <summary>A story moment's toast (the Pale Concord reveal): a major notice, with an optional detail
    /// line. A key with no text is dropped rather than shown raw.</summary>
    private void OnStoryToast(Narrative.StoryToastRequestedEvent e)
    {
        if (e.TextKey.Length == 0 || !Loc.Has(e.TextKey))
        {
            return;
        }

        Push(
            Loc.T(e.TextKey), UiTheme.Accent, NoticeCategory.Major,
            secondary: e.DetailKey.Length > 0 && Loc.Has(e.DetailKey) ? Loc.T(e.DetailKey) : null);
    }

    /// <summary>A companion's reaction line. It is something said, so with subtitles on it is
    /// captioned under the speaker's name (<see cref="SubtitleLayer"/>); otherwise it is a
    /// portrait-less toast: the line, and who said it beneath. A line raised under a menu or in a
    /// conversation is a toast too, because the feed holds it until the player is back in the world
    /// and a reaction line is said once per save.
    /// Public so a harness can drive it through the feed's own path.</summary>
    public void PushBark(string companionId, string textKey)
    {
        if (textKey.Length == 0 || !Loc.Has(textKey))
        {
            return;
        }

        string name = CompanionDatabase.Get(companionId) is { } companion ? Loc.T(companion.NameKey) : string.Empty;
        bool attending = !UiState.MenuOpen && GameManager.Instance?.State == GameState.Playing;
        if (attending && SubtitleLayer.TryShow(name.Length > 0 ? name : null, Loc.T(textKey), 0f))
        {
            return;
        }

        Push(
            Loc.T(textKey), UiTheme.Accent, NoticeCategory.Bark,
            secondary: name.Length > 0 ? Loc.TF("questui.bark_by", name) : null);
    }

    private void OnWorldEventStarted(WorldEventStartedEvent e) =>
        Push(Loc.TF("notify.event_started", Loc.T(e.NameKey)), UiTheme.Accent, NoticeCategory.Warning);

    private void OnWorldEventEnded(WorldEventEndedEvent e) =>
        Push(Loc.TF(e.Completed ? "notify.event_resolved" : "notify.event_failed", Loc.T(e.NameKey)),
            e.Completed ? UiTheme.Good : UiTheme.Bad,
            e.Completed ? NoticeCategory.Reward : NoticeCategory.Warning);

    /// <summary>Discovery feedback is reserved for places that define the journey: settlements,
    /// wilds, dungeons, mines and landmarks. Detail-tier counters and services still appear on the
    /// map, but announcing each one would turn a market arrival into a wall of toast.</summary>
    private void OnLocationDiscovered(LocationDiscoveredEvent e)
    {
        if (ShouldAnnounceDiscovery(e.Location.RevealWithCell, e.Location.EffectiveTier))
        {
            Push(Loc.TF("notify.location_discovered", Loc.T(e.Location.NameKey)), UiTheme.Accent, NoticeCategory.Reward);
        }
    }

    /// <summary>The discovery-feed noise gate, public so the content-independent rule is pinned by
    /// tests. Reveal-with-cell records are prior map knowledge (and arrive in a bulk region stream),
    /// while detail records are counters and services; neither is an arrival worth interrupting.</summary>
    public static bool ShouldAnnounceDiscovery(bool revealWithCell, MapTier tier) =>
        !revealWithCell && tier != MapTier.Detail;

    // --- BEGIN ics:save-ui: save and load feedback -------------------------------------------
    // Every save says where it went: an autosave quietly, a quick or manual save as a confirmation,
    // and a save that did not land as a warning with its reason. A toast pushed while the game is
    // paused waits for play to resume, so the pause menu also writes its own result inline.
    private void SubscribeSave(EventBus? bus)
    {
        bus?.Subscribe<SaveFailedEvent>(OnSaveFailed);
        bus?.Subscribe<SaveNoticeEvent>(OnSaveNotice);
        AddChild(new SaveIndicator { Name = "SaveIndicator" });
    }

    private void UnsubscribeSave(EventBus bus)
    {
        bus.Unsubscribe<SaveFailedEvent>(OnSaveFailed);
        bus.Unsubscribe<SaveNoticeEvent>(OnSaveNotice);
    }

    private void OnGameSaved(GameSavedEvent e)
    {
        if (e.IsAutosave)
        {
            Push(Loc.T("notify.autosaved"), UiTheme.Dim);
            return;
        }

        switch (Embervale.Save.SaveSlots.KindOf(e.Slot))
        {
            case Embervale.Save.SaveKind.Quick:
                Push(Loc.T("notify.quicksaved"), UiTheme.Good);
                break;
            case Embervale.Save.SaveKind.Manual:
                Push(Loc.TF("notify.saved_to", SaveSlotPanel.SlotLabel(e.Slot)), UiTheme.Good);
                break;
        }
    }

    // A refused or failed autosave retries on its own cadence, so only a genuine write failure is
    // worth a warning there; a block (boss fight, conversation) just means it is waiting its turn.
    private void OnSaveFailed(SaveFailedEvent e)
    {
        bool autosave = Embervale.Save.SaveSlots.KindOf(e.Slot) == Embervale.Save.SaveKind.Auto;
        if (autosave && e.ReasonKey != Embervale.Save.SaveManager.ReasonWriteFailed)
        {
            return;
        }

        Push(
            Loc.T(autosave ? "notify.autosave_failed" : "notify.save_failed"), UiTheme.Bad, NoticeCategory.Warning,
            secondary: Loc.Has(e.ReasonKey) ? Loc.T(e.ReasonKey) : null);
    }

    private void OnSaveNotice(SaveNoticeEvent e) =>
        Push(e.Text, e.Warning ? UiTheme.Bad : UiTheme.Dim, e.Warning ? NoticeCategory.Warning : NoticeCategory.Minor);

    // --- END ics:save-ui -----------------------------------------------------------------------

    // Companion name keys are Loc keys (like quest titles), so they resolve at display time.
    private void OnCompanionRecruited(CompanionRecruitedEvent e) =>
        Push(Loc.TF("notify.companion_joined", Loc.T(e.NameKey)), UiTheme.Good);

    private void OnCompanionDismissed(CompanionDismissedEvent e) =>
        Push(Loc.TF("notify.companion_left", Loc.T(e.NameKey)), UiTheme.Dim);

    private void OnCompanionDowned(CompanionDownedEvent e) =>
        Push(Loc.TF(e.Downed ? "notify.companion_downed" : "notify.companion_recovered", Loc.T(e.NameKey)),
            e.Downed ? UiTheme.Bad : UiTheme.Good);

    private void OnCompanionOrder(CompanionOrderIssuedEvent e) =>
        Push(Loc.TF("notify.companion_order", Loc.T(CompanionOrders.NameKey(e.Stance))), UiTheme.Accent);

    // Only a *tier* crossing toasts — every point of loyalty would be noise.
    private void OnCompanionLoyalty(CompanionLoyaltyTierChangedEvent e) =>
        Push(
            Loc.TF(
                e.Improved ? "notify.companion_loyalty_up" : "notify.companion_loyalty_down",
                Loc.T(CompanionDatabase.Get(e.CompanionId)?.NameKey ?? e.CompanionId),
                Loc.T(CompanionLoyalty.NameKey(e.Tier))),
            e.Improved ? UiTheme.Good : UiTheme.Bad);

    // 38R2. A wager opens no window, so this line IS the result — without it a loss is a gold counter
    // falling for no stated reason, which is what a player reports as a bug. Good/Bad rather than
    // Accent, because which way it went is the entire content.
    private void OnWagerSettled(WagerSettledEvent e) =>
        Push(
            Loc.TF(e.Won ? "notify.wager_won" : "notify.wager_lost", e.HouseName, e.Gold),
            e.Won ? UiTheme.Good : UiTheme.Bad);

    // 38T. The last cart of a haul is indistinguishable from the one before it, and what it bought —
    // prices at the far end going back to normal — is only visible to a player who goes and looks.
    private void OnShockRelieved(SupplyShockRelievedEvent e) =>
        Push(Loc.TF("notify.shock_relieved", Loc.T($"trade.tag.{e.Tag}")), UiTheme.Good);

    // 39A. An EMPTY key is the load path saying "restore this, do not narrate it" — the same rule
    // that keeps a reloaded save from toasting "Kael joins you" every time it is opened.
    private void OnMountChanged(MountChangedEvent e)
    {
        if (e.MessageKey.Length > 0)
        {
            Push(Loc.T(e.MessageKey), e.Mounted ? UiTheme.Accent : UiTheme.Dim);
        }
    }

    private void OnMountRefused(MountRefusedEvent e) => Push(Loc.T(e.ReasonKey), UiTheme.Bad);

    private void OnWorldHazard(WorldHazardNoticeEvent e) =>
        Push(Loc.T(e.ReasonKey), UiTheme.Bad, NoticeCategory.Warning);

    private void OnBlessingClaimed(BlessingClaimedEvent e) =>
        Push(Loc.TF("notify.blessing_received", Loc.T(e.Shrine.BlessingNameKey)), UiTheme.Good, NoticeCategory.Reward);

    private void OnShrineAlreadyVisited(ShrineAlreadyVisitedEvent e) =>
        Push(Loc.TF("notify.shrine_already_visited", Loc.T(e.Shrine.NameKey)), UiTheme.Dim);

    // The god's own words, not a template around the shrine's name: six refusals in six voices is
    // the whole point of authoring a key per shrine rather than one shared line.
    private void OnShrineRefused(ShrineRefusedEvent e) => Push(Loc.T(e.Shrine.RefusalKey), UiTheme.Bad);

    /// <summary>Queues a notice. <paramref name="collapseKey"/> names what it is about when the words
    /// alone do not (two pickups of one item are the same notice whatever the quantities), and
    /// <paramref name="quantity"/> is how much it adds to the count of a notice it repeats.</summary>
    private void Push(
        string text, Color color, NoticeCategory category = NoticeCategory.Minor, string? secondary = null,
        string? cue = null, string? collapseKey = null, int quantity = 1, bool journal = false)
    {
        // Hidden toasts still let a warning through: it is the only place a failed save is said.
        if (category != NoticeCategory.Warning && GameHud.ElementMode(HudElement.Toasts) == HudElementMode.Hidden)
        {
            return;
        }

        string key = collapseKey ?? (secondary == null ? text : $"{text}\n{secondary}");
        if (_coalesced.TryGetValue(key, out Notice? existing))
        {
            existing.Count += quantity;
            return;
        }

        // Already on screen: the toast the player is reading takes the count and starts its dwell again.
        if (_shown.TryGetValue(key, out Toast? live) && IsInstanceValid(live) && live.Bump(live.Count + quantity))
        {
            return;
        }

        // Burst protection keeps warnings and major beats, dropping the oldest minor fact first.
        if (_queue.Count >= MaxQueued && category == NoticeCategory.Minor)
        {
            return;
        }

        var notice = new Notice
        {
            Text = text, Accent = color, Category = category, Secondary = secondary, Cue = cue, Key = key,
            Journal = journal, Count = Mathf.Max(1, quantity),
        };
        _queue.Enqueue(notice, critical: category == NoticeCategory.Warning);
        _coalesced[key] = notice;
        PresentQueued();
    }

    private void PresentQueued()
    {
        if (!IsInsideTree() || IsQueuedForDeletion())
        {
            return;
        }

        // Menus and conversations already own the player's attention. Preserve the event and reveal
        // it after the protected state closes instead of drawing over prose or critical choices.
        if (UiState.MenuOpen || GameManager.Instance?.State != GameState.Playing)
        {
            return;
        }

        // A fight holds back everything but warnings; they are released, in order, when it ends.
        bool inCombat = _queue.Count > 0 && InCombat();
        PlaceStack();
        while (LiveToasts() < MaxVisible && Fits() && _queue.TryTake(inCombat, out Notice notice))
        {
            _coalesced.Remove(notice.Key);
            Present(notice);
        }
    }

    private void Present(Notice notice)
    {
        var toast = new Toast
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            Accent = notice.Accent,
            Dwell = ToastRules.Dwell(notice.Text, notice.Secondary, ToastDuration()),
        };

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        if (notice.Category != NoticeCategory.Bark)
        {
            row.AddChild(UiIcon.Create(IconFor(notice.Category), 22f, notice.Accent));
        }

        var copy = new VBoxContainer();
        copy.AddThemeConstantOverride("separation", UiTheme.LineGap);
        Label label = UiTheme.HudInk(notice.Category is NoticeCategory.Major or NoticeCategory.Quest
            ? UiTheme.Header(notice.Text)
            : UiTheme.Body(notice.Text));
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(ToastTextWidth, 0f);
        copy.AddChild(label);
        if (notice.Secondary != null)
        {
            Label second = UiTheme.HudInk(UiTheme.Caption(notice.Secondary, UiTheme.Dim));
            second.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            second.CustomMinimumSize = new Vector2(ToastTextWidth, 0f);
            copy.AddChild(second);
        }

        // A quest notice names the key that opens the journal: where the rest of it is.
        if (notice.Journal)
        {
            var hint = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            hint.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            var glyph = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            hint.AddChild(glyph);
            hint.AddChild(UiTheme.HudInk(UiTheme.Caption(Loc.T("hudc.toast.journal"), UiTheme.Dim)));
            copy.AddChild(hint);
            toast.GlyphSlot = glyph;
            toast.GlyphAction = GameInput.Journal;
        }

        row.AddChild(copy);

        // The count sits beside the words it multiplies, in the text colour: it is information, not
        // decoration, and it is a number as well as a place in a stack.
        Label count = UiTheme.HudInk(UiTheme.Body(string.Empty, UiTheme.Text));
        count.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        count.Visible = false;
        row.AddChild(count);
        toast.CountLabel = count;
        toast.SetCount(notice.Count);
        toast.AddContent(row);

        if (notice.Cue != null)
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(notice.Cue, Vector3.Zero));
        }

        _visible++;
        _shown[notice.Key] = toast;
        toast.TreeExited += () =>
        {
            _visible = Mathf.Max(0, _visible - 1);
            if (_shown.TryGetValue(notice.Key, out Toast? held) && ReferenceEquals(held, toast))
            {
                _shown.Remove(notice.Key);
            }

            // Deferred: a toast also leaves the tree when the whole HUD is torn down, and presenting
            // the next one from inside that exit adds a child to a parent that is mid-teardown.
            Callable.From(PresentQueued).CallDeferred();
        };
        _stack.AddChild(toast);
    }

    /// <summary>Starts the feed under the tracker, wherever the tracker ends, instead of at a fixed offset
    /// that a longer tracker ran into.</summary>
    private void PlaceStack()
    {
        float top = UiTheme.SpaceLg;
        if (Hud() is { } hud)
        {
            top = Mathf.Max(top, hud.TopRightBottom) + UiTheme.HudGap;
        }

        _stack.OffsetTop = top;
        _stack.OffsetBottom = top;
    }

    /// <summary>Room between the top of the stack and the minimap, which sits in the same right-hand column.</summary>
    private float Room() =>
        Hud() is { } hud ? hud.BottomRightTop - UiTheme.HudGap - _stack.OffsetTop : float.MaxValue;

    /// <summary>Height the stack takes now, counting toasts that are still fading out: they hold their space
    /// until they leave, so a new toast has to clear them too.</summary>
    private float StackHeight()
    {
        float height = 0f;
        int count = 0;
        int children = _stack.GetChildCount();
        for (int i = 0; i < children; i++) // by index: GetChildren() builds an array per call
        {
            if (_stack.GetChild(i) is Control { Visible: true } toast)
            {
                height += toast.GetCombinedMinimumSize().Y;
                count++;
            }
        }

        return height + (Mathf.Max(0, count - 1) * UiTheme.SpaceSm);
    }

    /// <summary>Whether one more toast fits above the minimap. An empty stack always admits one, so a tall
    /// tracker can never stop notices altogether.</summary>
    private bool Fits() =>
        _stack.GetChildCount() == 0 || StackHeight() + UiTheme.SpaceSm + ToastSlot <= Room();

    private int LiveToasts()
    {
        int live = 0;
        int children = _stack.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            if (_stack.GetChild(i) is Toast { Expiring: false })
            {
                live++;
            }
        }

        return live;
    }

    /// <summary>The tracker can grow after a toast was admitted (a quest event adds its rows a frame later), which
    /// would push the stack into the minimap. When what is showing no longer fits, the oldest toast is
    /// let go early; the queue refills the stack as space allows.</summary>
    private void ShedOverflow()
    {
        if (LiveToasts() <= 1 || StackHeight() <= Room())
        {
            return;
        }

        int children = _stack.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            if (_stack.GetChild(i) is Toast { Expiring: false } oldest)
            {
                oldest.Expedite();
                return;
            }
        }
    }

    private static float ToastDuration() =>
        Core.Services.ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings.Current.ToastDuration
            : 1f;

    private GameHud? Hud()
    {
        if (_hud is not null && IsInstanceValid(_hud))
        {
            return _hud;
        }

        _hud = null;
        foreach (Node sibling in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (sibling is GameHud hud)
            {
                _hud = hud;
                break;
            }
        }

        return _hud;
    }

    private static UiIcon.Kind IconFor(NoticeCategory category) => category switch
    {
        NoticeCategory.Quest => UiIcon.Kind.Quest,
        NoticeCategory.Warning => UiIcon.Kind.Warning,
        NoticeCategory.Reward => UiIcon.Kind.Currency,
        NoticeCategory.Major => UiIcon.Kind.Spell,
        _ => UiIcon.Kind.Waypoint,
    };
}
