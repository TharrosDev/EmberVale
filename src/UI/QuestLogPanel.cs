using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The quest journal: a read-only overlay toggled with the <c>journal</c> action (J).
/// Unlike the character screen it is non-modal — it neither captures the mouse nor sets
/// <see cref="UiState.MenuOpen"/>, so it can be left up while playing. It lists active
/// quests with per-objective progress and a completed section, rebuilding from a dirty
/// flag (never during a signal) whenever quest events fire or a game is loaded.
/// </summary>
public partial class QuestLogPanel : CanvasLayer
{
    private QuestLogComponent? _log;
    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;
    private bool _dirty = true;

    public override void _Ready()
    {
        _panel = UiTheme.Panel();
        _panel.Visible = false;
        _panel.Position = new Vector2(520, 16);
        _panel.CustomMinimumSize = new Vector2(360, 0);
        AddChild(_panel);

        MarginContainer margin = UiTheme.Padding(12);
        _panel.AddChild(margin);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 3);
        margin.AddChild(_list);

        EventBus.Instance?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Subscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        EventBus.Instance?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Unsubscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        EventBus.Instance?.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public void SetQuestLog(QuestLogComponent? log)
    {
        _log = log;
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(GameInput.Journal))
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible)
            {
                _dirty = true;
            }
        }

        if (_panel.Visible && _dirty)
        {
            Rebuild();
        }
    }

    private void OnQuestStarted(QuestStartedEvent e) => _dirty = true;

    private void OnObjectiveAdvanced(QuestObjectiveAdvancedEvent e) => _dirty = true;

    private void OnQuestCompleted(QuestCompletedEvent e) => _dirty = true;

    private void OnGameLoaded(GameLoadedEvent e) => _dirty = true;

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        AddHeader("QUEST JOURNAL   (J to close)");

        if (_log == null || _log.Quests.Count == 0)
        {
            AddLine("(no quests)");
            return;
        }

        bool anyActive = false;
        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Status == QuestStatus.Active)
            {
                anyActive = true;
                BuildQuest(progress);
            }
        }

        if (!anyActive)
        {
            AddLine("(no active quests)");
        }

        bool completedHeader = false;
        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Status != QuestStatus.Completed)
            {
                continue;
            }

            if (!completedHeader)
            {
                AddHeader("COMPLETED");
                completedHeader = true;
            }

            AddLine($"✓ {progress.Quest.Title}", new Color(0.55f, 0.75f, 0.55f));
        }
    }

    private void BuildQuest(QuestProgress progress)
    {
        AddLine(progress.Quest.Title, new Color(0.95f, 0.85f, 0.45f));

        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveResource objective = objectives[i];
            bool done = progress.IsObjectiveComplete(i);
            string mark = done ? "✓" : "•";
            AddLine($"   {mark} {objective.ShortLabel()}  {progress.Counts[i]}/{objective.RequiredCount}",
                done ? new Color(0.55f, 0.75f, 0.55f) : Colors.White);
        }
    }

    private void AddHeader(string text)
    {
        _list.AddChild(UiTheme.Header(text));
    }

    private void AddLine(string text, Color? color = null)
    {
        _list.AddChild(UiTheme.Body(text, color));
    }
}
