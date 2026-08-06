using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The quest journal: a read-only overlay toggled with the <c>journal</c> action (J), built on
/// the 30.5F <see cref="UiPanel"/> framework. Unlike the character screen it is non-modal — it
/// neither captures the mouse nor sets <c>UiState.MenuOpen</c>, so it can be left up while
/// playing. It lists active quests with per-objective progress and a completed section.
/// </summary>
public partial class QuestLogPanel : UiPanel
{
    private QuestLogComponent? _log;
    private VBoxContainer _list = null!;

    protected override bool Modal => false;

    protected override string? ToggleAction => GameInput.Journal;

    protected override void BuildShell(PanelContainer shell)
    {
        // Top-left, below the HUD's clock/weather widget (30.5B placement sweep).
        shell.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        shell.OffsetLeft = 16;
        shell.OffsetTop = 64;
        shell.CustomMinimumSize = new Vector2(360, 0);

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 3);
        margin.AddChild(_list);
    }

    protected override void OnReady()
    {
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
        MarkDirty();
    }

    private void OnQuestStarted(QuestStartedEvent e) => MarkDirty();

    private void OnObjectiveAdvanced(QuestObjectiveAdvancedEvent e) => MarkDirty();

    private void OnQuestCompleted(QuestCompletedEvent e) => MarkDirty();

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        _list.AddChild(UiTheme.Title(Loc.T("questlog.title")));

        if (_log == null || _log.Quests.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.none"), UiTheme.Dim));
            return;
        }

        // Main / Side / Completed.
        //
        // ⚠️ There is deliberately **no Failed section**. `QuestStatus` has exactly two members,
        // Active and Completed - nothing in the game can fail a quest, so a Failed heading would be
        // a permanently empty promise. Same call as the omitted Contracts and Exploration headings:
        // the journal shows the states the data actually has. Add the section when the state exists.
        int active = 0;
        active += BuildSection(Loc.T("questlog.main"), UiTheme.QuestMain, true);
        active += BuildSection(Loc.T("questlog.side"), UiTheme.QuestSide, false);

        if (active == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.no_active"), UiTheme.Dim));
        }

        BuildCompleted();
    }

    /// <summary>Builds one active-quest section, returning how many it drew so the caller can tell
    /// whether the whole journal is empty of live work.</summary>
    private int BuildSection(string title, Color tint, bool main)
    {
        var matching = new List<QuestProgress>();
        foreach (QuestProgress progress in _log!.Quests)
        {
            if (progress.Status == QuestStatus.Active && progress.Quest.IsMainQuest == main)
            {
                matching.Add(progress);
            }
        }

        if (matching.Count == 0)
        {
            return 0;
        }

        _list.AddChild(UiTheme.SectionRule(title));
        foreach (QuestProgress progress in matching)
        {
            _list.AddChild(BuildQuestCard(progress, tint));
        }

        return matching.Count;
    }

    private void BuildCompleted()
    {
        var done = new List<QuestProgress>();
        foreach (QuestProgress progress in _log!.Quests)
        {
            if (progress.Status == QuestStatus.Completed)
            {
                done.Add(progress);
            }
        }

        if (done.Count == 0)
        {
            return;
        }

        _list.AddChild(UiTheme.SectionRule(Loc.T("questlog.completed")));
        foreach (QuestProgress progress in done)
        {
            PanelContainer card = UiTheme.Card(UiTheme.QuestComplete);
            MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
            pad.AddChild(UiTheme.Body($"✓ {Loc.T(progress.Quest.Title)}", UiTheme.QuestComplete));
            card.AddChild(pad);
            _list.AddChild(card);
        }
    }

    /// <summary>One active quest: title on a coloured spine, then an objective row per goal with a
    /// progress bar for anything counting past one.</summary>
    private Control BuildQuestCard(QuestProgress progress, Color tint)
    {
        PanelContainer card = UiTheme.Card(tint);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        Label title = UiTheme.Body(Loc.T(progress.Quest.Title), tint);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        col.AddChild(title);

        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveResource objective = objectives[i];
            bool done = progress.IsObjectiveComplete(i);
            int have = progress.Counts[i];
            int required = Mathf.Max(1, objective.RequiredCount);

            col.AddChild(UiTheme.Caption(
                $"{(done ? "✓" : "•")} {Loc.T(objective.ShortLabel())}  {have}/{objective.RequiredCount}",
                done ? UiTheme.QuestComplete : UiTheme.Text));

            // A bar only where there is something to fill. "1/1" is a tick, not a gauge.
            if (objective.RequiredCount > 1)
            {
                ProgressBar bar = UiTheme.Bar(done ? UiTheme.QuestComplete : tint, 300f);
                bar.CustomMinimumSize = new Vector2(0f, 3f);
                bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bar.Value = Mathf.Clamp(have / (double)required, 0d, 1d);
                col.AddChild(bar);
            }
        }

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
        pad.AddChild(col);
        card.AddChild(pad);
        return card;
    }
}
