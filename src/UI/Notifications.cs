using Embervale.Core.Events;
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
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 6);
        _stack.AnchorLeft = 0.5f;
        _stack.AnchorRight = 0.5f;
        _stack.AnchorTop = 0f;
        _stack.AnchorBottom = 0f;
        _stack.GrowHorizontal = Control.GrowDirection.Both;
        _stack.GrowVertical = Control.GrowDirection.End;
        _stack.OffsetTop = 110;
        _stack.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(_stack);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        bus?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<WorldEventStartedEvent>(OnWorldEventStarted);
        bus?.Subscribe<WorldEventEndedEvent>(OnWorldEventEnded);
        bus?.Subscribe<GameSavedEvent>(OnGameSaved);
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
    }

    private void OnLeveledUp(LeveledUpEvent e) => Push($"Level up!  You are now level {e.NewLevel}", UiTheme.Accent);

    private void OnQuestStarted(QuestStartedEvent e) => Push($"Quest started:  {e.Quest.Title}", UiTheme.Text);

    private void OnQuestCompleted(QuestCompletedEvent e) => Push($"Quest complete:  {e.Quest.Title}", UiTheme.Good);

    private void OnWorldEventStarted(WorldEventStartedEvent e) => Push($"★ {e.DisplayName}", UiTheme.Accent);

    private void OnWorldEventEnded(WorldEventEndedEvent e) =>
        Push(e.Completed ? $"{e.DisplayName} — resolved!" : $"{e.DisplayName} — failed", e.Completed ? UiTheme.Good : UiTheme.Bad);

    // Only the autosave cadence (Phase 24D) toasts; manual quicksaves (F5) stay quiet.
    private void OnGameSaved(GameSavedEvent e)
    {
        if (e.IsAutosave)
        {
            Push("Autosaved", UiTheme.Dim);
        }
    }

    private void Push(string text, Color color)
    {
        var toast = new Toast { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };

        MarginContainer pad = UiTheme.Padding(8);
        Label label = UiTheme.Body(text, color);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        pad.AddChild(label);
        toast.AddChild(pad);

        _stack.AddChild(toast);
    }
}
