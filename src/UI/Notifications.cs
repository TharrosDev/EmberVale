using Embervale.Companions;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Progression;
using Embervale.Quests;
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
    private VBoxContainer _stack = null!;

    public override void _Ready()
    {
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
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus?.Subscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus?.Subscribe<GameSavedEvent>(OnGameSaved);
        bus?.Subscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus?.Subscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus?.Subscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus?.Subscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
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
        bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Unsubscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus.Unsubscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        bus.Unsubscribe<CompanionRecruitedEvent>(OnCompanionRecruited);
        bus.Unsubscribe<CompanionDismissedEvent>(OnCompanionDismissed);
        bus.Unsubscribe<CompanionDownedEvent>(OnCompanionDowned);
        bus.Unsubscribe<CompanionOrderIssuedEvent>(OnCompanionOrder);
        bus.Unsubscribe<CompanionLoyaltyTierChangedEvent>(OnCompanionLoyalty);
    }

    private void OnLeveledUp(LeveledUpEvent e) => Push(Loc.TF("notify.levelup", e.NewLevel), UiTheme.Accent);

    // Quest.Title is a Loc key (data-authored), so it must be resolved before display.
    private void OnQuestStarted(QuestStartedEvent e) =>
        Push(Loc.TF("notify.quest_started", Loc.T(e.Quest.Title)), UiTheme.Text);

    private void OnQuestCompleted(QuestCompletedEvent e) =>
        Push(Loc.TF("notify.quest_complete", Loc.T(e.Quest.Title)), UiTheme.Good);

    private void OnWorldEventStarted(WorldEventStartedEvent e) => Push(Loc.TF("notify.event_started", e.DisplayName), UiTheme.Accent);

    private void OnWorldEventEnded(WorldEventEndedEvent e) =>
        Push(Loc.TF(e.Completed ? "notify.event_resolved" : "notify.event_failed", e.DisplayName),
            e.Completed ? UiTheme.Good : UiTheme.Bad);

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

    private void Push(string text, Color color)
    {
        var toast = new Toast { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };

        MarginContainer pad = UiTheme.Padding(8);
        Label label = UiTheme.Body(text, color);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        pad.AddChild(label);
        toast.AddContent(pad);

        _stack.AddChild(toast);
    }
}
