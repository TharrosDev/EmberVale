using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Economy;
using Embervale.Localization;
using Embervale.Movement;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Shrines;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The toast/notification feed: a top-centre stack of transient <see cref="Toast"/> chips
/// announcing discrete, meaningful moments — level-ups, quest start/completion, and world
/// events beginning/ending. Event-driven, so any system that raises one of these is surfaced
/// to the player without coupling. Built through <see cref="UiTheme"/>.
/// </summary>
public partial class Notifications : CanvasLayer
{
    private const int MaxVisible = 3;
    private const int MaxQueued = 12;

    private enum NoticeCategory { Minor, Reward, Quest, Warning, Major }

    private sealed class Notice
    {
        public required string Text { get; init; }
        public required Color Accent { get; init; }
        public required NoticeCategory Category { get; init; }
        public int Count { get; set; } = 1;
    }

    private VBoxContainer _stack = null!;
    private readonly Queue<Notice> _queue = new();
    private readonly Dictionary<string, Notice> _coalesced = new();
    private int _visible;

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
        _stack.OffsetTop = 190;
        _stack.OffsetBottom = 190;
        AddChild(_stack);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        bus?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        bus?.Subscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<QuestFailedEvent>(OnQuestFailed);
        bus?.Subscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus?.Subscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus?.Subscribe<LocationDiscoveredEvent>(OnLocationDiscovered);
        bus?.Subscribe<GameSavedEvent>(OnGameSaved);
        bus?.Subscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus?.Subscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus?.Subscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus?.Subscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
        bus?.Subscribe<WagerSettledEvent>(OnWagerSettled);
        bus?.Subscribe<SupplyShockRelievedEvent>(OnShockRelieved);
        bus?.Subscribe<MountChangedEvent>(OnMountChanged);
        bus?.Subscribe<MountRefusedEvent>(OnMountRefused);
        bus?.Subscribe<BlessingClaimedEvent>(OnBlessingClaimed);
        bus?.Subscribe<ShrineAlreadyVisitedEvent>(OnShrineAlreadyVisited);
        bus?.Subscribe<ShrineRefusedEvent>(OnShrineRefused);
    }

    public override void _Process(double delta)
    {
        bool protectedState = UiState.MenuOpen || GameManager.Instance?.State != GameState.Playing;
        _stack.Visible = !protectedState;
        if (!protectedState)
        {
            PresentQueued();
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
        bus.Unsubscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
        bus.Unsubscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus.Unsubscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus.Unsubscribe<LocationDiscoveredEvent>(OnLocationDiscovered);
        bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        bus.Unsubscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus.Unsubscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus.Unsubscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus.Unsubscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus.Unsubscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
        bus.Unsubscribe<WagerSettledEvent>(OnWagerSettled);
        bus.Unsubscribe<SupplyShockRelievedEvent>(OnShockRelieved);
        bus.Unsubscribe<MountChangedEvent>(OnMountChanged);
        bus.Unsubscribe<MountRefusedEvent>(OnMountRefused);
        bus.Unsubscribe<BlessingClaimedEvent>(OnBlessingClaimed);
        bus.Unsubscribe<ShrineAlreadyVisitedEvent>(OnShrineAlreadyVisited);
        bus.Unsubscribe<ShrineRefusedEvent>(OnShrineRefused);
    }

    private void OnLeveledUp(LeveledUpEvent e) =>
        Push(Loc.TF("notify.levelup", e.NewLevel), UiTheme.Accent, NoticeCategory.Major);

    // Quest.Title is a Loc key (data-authored), so it must be resolved before display.
    private void OnQuestStarted(QuestStartedEvent e) =>
        Push(Loc.TF("notify.quest_started", Loc.T(e.Quest.Title)), UiTheme.Text, NoticeCategory.Quest);

    /// <summary>
    /// An objective ticking over (§24). Reuses this feed rather than adding a second notification
    /// system (§75) — the toast stack already knows how to queue, hold and fade.
    ///
    /// ⚠️ <b>Only on completion, and only for multi-objective quests.</b> Toasting every increment
    /// puts nine chips on screen for a ten-pelt errand, which is how a feed stops being read at all;
    /// the tracker's own progress bar is the at-a-glance channel for counting, and that is what it
    /// was added for in 37.5B. A single-objective quest is skipped because finishing its one
    /// objective IS finishing the quest, and <see cref="OnQuestCompleted"/> already says so — two
    /// chips for one event reads as a bug.
    /// </summary>
    private void OnObjectiveAdvanced(QuestObjectiveAdvancedEvent e)
    {
        if (e.Count < e.Required)
        {
            return;
        }

        var objectives = e.Quest.ObjectiveList();
        if (objectives.Count <= 1 || e.ObjectiveIndex < 0 || e.ObjectiveIndex >= objectives.Count)
        {
            return;
        }

        Push(
            Loc.TF("hud.quest.objective_done", Loc.T(objectives[e.ObjectiveIndex].ShortLabel())),
            UiTheme.QuestComplete, NoticeCategory.Quest);
    }

    private void OnQuestCompleted(QuestCompletedEvent e) =>
        Push(Loc.TF("notify.quest_complete", Loc.T(e.Quest.Title)), UiTheme.Good, NoticeCategory.Major);

    /// <summary>A quest lost (41B). It shares the world event's failure colour rather than the
    /// companion-downed one: what the player needs told apart here is "this ended badly" from "this
    /// ended well", and the downed toast that caused an escort failure has already fired beside it.</summary>
    private void OnQuestFailed(QuestFailedEvent e) =>
        Push(Loc.TF("notify.quest_failed", Loc.T(e.Quest.Title)), UiTheme.Bad, NoticeCategory.Warning);

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

    // Only the autosave cadence (Phase 24D) toasts; manual quicksaves (F5) stay quiet.
    private void OnGameSaved(GameSavedEvent e)
    {
        if (e.IsAutosave)
        {
            Push(Loc.T("notify.autosaved"), UiTheme.Dim);
        }
    }

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

    private void OnBlessingClaimed(BlessingClaimedEvent e) =>
        Push(Loc.TF("notify.blessing_received", Loc.T(e.Shrine.BlessingNameKey)), UiTheme.Good, NoticeCategory.Reward);

    private void OnShrineAlreadyVisited(ShrineAlreadyVisitedEvent e) =>
        Push(Loc.TF("notify.shrine_already_visited", Loc.T(e.Shrine.NameKey)), UiTheme.Dim);

    // The god's own words, not a template around the shrine's name: six refusals in six voices is
    // the whole point of authoring a key per shrine rather than one shared line.
    private void OnShrineRefused(ShrineRefusedEvent e) => Push(Loc.T(e.Shrine.RefusalKey), UiTheme.Bad);

    private void Push(string text, Color color, NoticeCategory category = NoticeCategory.Minor)
    {
        if (_coalesced.TryGetValue(text, out Notice? existing))
        {
            existing.Count++;
            return;
        }

        // Burst protection keeps warnings and major beats, dropping the oldest minor fact first.
        if (_queue.Count >= MaxQueued && category == NoticeCategory.Minor)
        {
            return;
        }

        var notice = new Notice { Text = text, Accent = color, Category = category };
        _queue.Enqueue(notice);
        _coalesced[text] = notice;
        PresentQueued();
    }

    private void PresentQueued()
    {
        // Menus and conversations already own the player's attention. Preserve the event and reveal
        // it after the protected state closes instead of drawing over prose or critical choices.
        if (UiState.MenuOpen || GameManager.Instance?.State != GameState.Playing)
        {
            return;
        }

        while (_visible < MaxVisible && _queue.Count > 0)
        {
            Notice notice = _queue.Dequeue();
            _coalesced.Remove(notice.Text);
            Present(notice);
        }
    }

    private void Present(Notice notice)
    {
        var toast = new Toast
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            Accent = notice.Accent,
            Life = notice.Category switch
            {
                NoticeCategory.Minor => 3.2,
                NoticeCategory.Warning => 5.5,
                NoticeCategory.Major => 6.0,
                _ => 4.5,
            },
        };

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceMd);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(UiIcon.Create(IconFor(notice.Category), 22f, notice.Accent));

        var copy = new VBoxContainer();
        copy.AddThemeConstantOverride("separation", 0);
        Label label = notice.Category is NoticeCategory.Major or NoticeCategory.Quest
            ? UiTheme.Header(notice.Text)
            : UiTheme.Body(notice.Text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(220f, 0f);
        copy.AddChild(label);
        if (notice.Count > 1)
        {
            copy.AddChild(UiTheme.Caption($"×{notice.Count}", UiTheme.Dim));
        }
        row.AddChild(copy);
        pad.AddChild(row);
        toast.AddContent(pad);

        _visible++;
        toast.TreeExited += () =>
        {
            _visible = Mathf.Max(0, _visible - 1);
            PresentQueued();
        };
        _stack.AddChild(toast);
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
