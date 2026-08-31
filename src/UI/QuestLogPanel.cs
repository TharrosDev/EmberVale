using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The quest journal: a modal, fully interactive list/detail workspace. It owns focus and the mouse
/// while open so track/untrack is equally reachable by mouse, keyboard and controller.
/// </summary>
public partial class QuestLogPanel : UiPanel
{
    private QuestLogComponent? _log;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private string? _selectedId;

    protected override string? ToggleAction => GameInput.Journal;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        UiTheme.ApplyScreenInset(shell);

        MarginContainer margin = UiTheme.Padding(UiTheme.SpaceLg);
        shell.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        root.AddChild(UiTheme.Title(Loc.T("questlog.title")));
        root.AddChild(UiTheme.Divider());
        margin.AddChild(root);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        root.AddChild(body);

        PanelContainer indexWell = UiTheme.Well();
        indexWell.CustomMinimumSize = new Vector2(330f, 0f);
        body.AddChild(indexWell);
        (ScrollContainer indexScroll, VBoxContainer indexList) = UiTheme.ScrollList();
        _list = indexList;
        MarginContainer indexPad = UiTheme.Padding(UiTheme.SpaceSm);
        indexPad.AddChild(indexScroll);
        indexWell.AddChild(indexPad);

        PanelContainer detailBand = UiTheme.Band(UiTheme.QuestMain);
        detailBand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailBand.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(detailBand);
        (ScrollContainer detailScroll, VBoxContainer detailList) = UiTheme.ScrollList();
        _detail = detailList;
        MarginContainer detailPad = UiTheme.Padding(UiTheme.SpaceLg);
        detailPad.AddChild(detailScroll);
        detailBand.AddChild(detailPad);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Subscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        EventBus.Instance?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);

        // ⚠️ CAUGHT BY A RENDERED FRAME, NOT BY REVIEW (41B). Without this line the journal keeps
        // showing a failed quest under ERRANDS, still labelled TRACKED, until some other quest event
        // happens to mark the panel dirty - while the toast says it failed and the HUD tracker has
        // already moved on. Three surfaces, two answers. A new state has to reach every surface that
        // draws the old one.
        EventBus.Instance?.Subscribe<QuestFailedEvent>(OnQuestFailed);
        EventBus.Instance?.Subscribe<QuestResetEvent>(OnQuestReset);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);

        // ⚠️ 41D, and this line exists because 41B shipped its absence. A quest's BRANCH changes on a
        // story flag, which is not a quest event at all — so a fork chosen while the journal is open
        // would leave the card showing the path the player just declined, until some unrelated quest
        // event happened to rebuild it. 41B's rule, one sub-phase later: the grep is not "who draws
        // quests" but "what else can change what a quest looks like".
        EventBus.Instance?.Subscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Unsubscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        EventBus.Instance?.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        EventBus.Instance?.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
        EventBus.Instance?.Unsubscribe<QuestResetEvent>(OnQuestReset);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        EventBus.Instance?.Unsubscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
    }

    public void SetQuestLog(QuestLogComponent? log)
    {
        _log = log;
        MarkDirty();
    }

    private void OnQuestStarted(QuestStartedEvent e) => MarkDirty();

    private void OnObjectiveAdvanced(QuestObjectiveAdvancedEvent e) => MarkDirty();

    private void OnQuestCompleted(QuestCompletedEvent e) => MarkDirty();

    private void OnQuestFailed(QuestFailedEvent e) => MarkDirty();

    private void OnQuestReset(QuestResetEvent e) => MarkDirty();

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    private void OnStoryFlagChanged(Dialogue.StoryFlagChangedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);
        UiTheme.ClearChildren(_detail);

        if (_log == null || _log.Quests.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.none"), UiTheme.Dim));
            _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Quest, Loc.T("questlog.none"), tint: UiTheme.Dim));
            return;
        }

        QuestProgress? selected = FindSelected();
        if (selected == null)
        {
            foreach (QuestProgress candidate in _log.Quests)
            {
                selected = candidate;
                _selectedId = candidate.Quest.Id;
                if (candidate.Status == QuestStatus.Active)
                {
                    break;
                }
            }
        }

        // Main / Side / Completed / Failed.
        //
        // ⚠️ The Failed section arrived with the state, not before it (41B). Until this sub-phase
        // `QuestStatus` had exactly two members and nothing in the game could fail a quest, so the
        // heading would have been a permanently empty promise - the same call as the still-omitted
        // Contracts and Exploration headings. The journal shows the states the data actually has,
        // which is invariant 28 read from the UI side: check whether the state exists before
        // building the presentation of it.
        int active = 0;
        active += BuildSection(Loc.T("questlog.main"), UiTheme.QuestMain, true);
        active += BuildSection(Loc.T("questlog.side"), UiTheme.QuestSide, false);

        if (active == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.no_active"), UiTheme.Dim));
        }

        BuildCompleted();
        BuildFailed();
        if (selected != null)
        {
            BuildDetail(selected);
        }
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
            _list.AddChild(QuestRow(progress, tint));
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
            _list.AddChild(QuestRow(progress, UiTheme.QuestComplete));
        }
    }

    /// <summary>
    /// The quests that ended badly (41B). Drawn like the completed list and below it — a failure is
    /// history, not work — but with the ✗ mark and <see cref="UiTheme.QuestFailed"/> beside
    /// completed's ✓, so the two are never told apart by colour alone (UI_STYLE §2).
    ///
    /// ⚠️ It says nothing about the quest being retakeable. That fact belongs to the giver's
    /// conversation, which reopens on its own because <c>CanStart</c> allows a failed quest — a
    /// journal line promising a second chance would be a second answer to a question the dialogue
    /// already owns.
    /// </summary>
    private void BuildFailed()
    {
        var lost = new List<QuestProgress>();
        foreach (QuestProgress progress in _log!.Quests)
        {
            if (progress.Status == QuestStatus.Failed)
            {
                lost.Add(progress);
            }
        }

        if (lost.Count == 0)
        {
            return;
        }

        _list.AddChild(UiTheme.SectionRule(Loc.T("questlog.failed")));
        foreach (QuestProgress progress in lost)
        {
            _list.AddChild(QuestRow(progress, UiTheme.QuestFailed));
        }
    }

    private QuestProgress? FindSelected()
    {
        if (_selectedId == null || _log == null)
        {
            return null;
        }

        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Quest.Id == _selectedId)
            {
                return progress;
            }
        }
        return null;
    }

    private Control QuestRow(QuestProgress progress, Color tint)
    {
        bool selected = progress.Quest.Id == _selectedId;
        Button row = UiTheme.Action(Loc.T(progress.Quest.Title));
        row.Alignment = HorizontalAlignment.Left;
        row.AddThemeColorOverride("font_color", selected ? tint : UiTheme.Text);
        StyleBoxFlat style = UiTheme.CardStyle(tint);
        if (selected)
        {
            style.BgColor = UiTheme.CardBg with { A = 1f };
            style.BorderWidthLeft = 4;
        }
        row.AddThemeStyleboxOverride("normal", style);
        row.Pressed += () =>
        {
            _selectedId = progress.Quest.Id;
            MarkDirty();
        };
        return row;
    }

    private void BuildDetail(QuestProgress progress)
    {
        Color tint = progress.Status == QuestStatus.Completed ? UiTheme.QuestComplete
            : progress.Status == QuestStatus.Failed ? UiTheme.QuestFailed
            : progress.Quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide;

        _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Quest, Loc.T(progress.Quest.Title), tint: tint));
        if (progress.Quest.Summary.Length > 0)
        {
            Label summary = UiTheme.Prose(Loc.T(progress.Quest.Summary), UiTheme.Text);
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detail.AddChild(summary);
        }

        if (progress.Status == QuestStatus.Active)
        {
            _detail.AddChild(TrackButton(progress));
        }

        _detail.AddChild(UiTheme.SectionRule(Loc.T("hud.quest")));
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (!progress.IsObjectiveInBranch(i))
            {
                continue;
            }

            ObjectiveResource objective = objectives[i];
            bool locked = !progress.IsObjectiveActive(i);
            bool done = progress.IsObjectiveComplete(i);
            int have = progress.Counts[i];
            int required = Mathf.Max(1, objective.RequiredCount);

            PanelContainer objectiveBand = UiTheme.Band(done ? UiTheme.QuestComplete : locked ? UiTheme.Dim : tint);
            var objectiveCopy = new VBoxContainer();
            objectiveCopy.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
            objectiveCopy.AddChild(UiTheme.IconLabel(
                locked ? UiIcon.Kind.Lock : done ? UiIcon.Kind.Quest : UiIcon.Kind.Waypoint,
                Loc.T(objective.ShortLabel()), $"{have}/{objective.RequiredCount}",
                done ? UiTheme.QuestComplete : locked ? UiTheme.Dim : UiTheme.Text));
            if (objective.RequiredCount > 1)
            {
                ProgressBar bar = UiTheme.Bar(done ? UiTheme.QuestComplete : tint, 360f);
                bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bar.Value = Mathf.Clamp(have / (double)required, 0d, 1d);
                objectiveCopy.AddChild(bar);
            }
            objectiveBand.AddChild(objectiveCopy);
            _detail.AddChild(objectiveBand);
        }

        if (progress.Quest.XpReward > 0 || progress.Quest.GoldReward > 0)
        {
            _detail.AddChild(UiTheme.SectionRule(Loc.T("questlog.rewards")));
            if (progress.Quest.XpReward > 0)
            {
                _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Spell,
                    Loc.TF("questlog.reward_xp", progress.Quest.XpReward), tint: UiTheme.Accent));
            }
            if (progress.Quest.GoldReward > 0)
            {
                _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Currency, $"{progress.Quest.GoldReward}", tint: UiTheme.Accent));
            }
        }
    }

    /// <summary>
    /// Follow-this-quest toggle. Shows which quest the HUD is currently on, and pressing it moves the
    /// tracker and the compass marker together (they read one authority since 39.5B).
    ///
    /// The state is carried by the label as well as the colour — "TRACKED" versus "TRACK" — because
    /// colour is never the only channel (UI_STYLE §2, brief §40).
    /// </summary>
    private Button TrackButton(QuestProgress progress)
    {
        bool tracked = ReferenceEquals(_log?.Tracked, progress);

        Button button = UiTheme.Action(Loc.T(tracked ? "questlog.untrack" : "questlog.track"));
        button.TooltipText = Loc.T("questlog.track_tip");
        button.AddThemeColorOverride("font_color", tracked ? UiTheme.Accent : UiTheme.Text);

        // Never rebuild inside a button signal (CLAUDE.md §8 / UiPanel) — flag it and let the
        // panel's own dirty loop redraw on the next frame.
        button.Pressed += () =>
        {
            _log?.Track(tracked ? null : progress.Quest.Id);
            MarkDirty();
        };

        return button;
    }

}
