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

        // ⚠️ CAUGHT BY A RENDERED FRAME, NOT BY REVIEW (41B). Without this line the journal keeps
        // showing a failed quest under ERRANDS, still labelled TRACKED, until some other quest event
        // happens to mark the panel dirty - while the toast says it failed and the HUD tracker has
        // already moved on. Three surfaces, two answers. A new state has to reach every surface that
        // draws the old one.
        EventBus.Instance?.Subscribe<QuestFailedEvent>(OnQuestFailed);
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

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    private void OnStoryFlagChanged(Dialogue.StoryFlagChangedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        _list.AddChild(UiTheme.Title(Loc.T("questlog.title")));

        if (_log == null || _log.Quests.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.none"), UiTheme.Dim));
            return;
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
            PanelContainer card = UiTheme.Card(UiTheme.QuestFailed);
            MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
            pad.AddChild(UiTheme.Body($"✗ {Loc.T(progress.Quest.Title)}", UiTheme.QuestFailed));
            card.AddChild(pad);
            _list.AddChild(card);
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

        var button = new Button
        {
            Text = Loc.T(tracked ? "questlog.tracked" : "questlog.track"),
            Disabled = tracked,
            TooltipText = Loc.T("questlog.track_tip"),
        };
        UiTheme.ApplyType(button, UiTheme.FontRole.Interface, UiTheme.CaptionFontSize);
        button.AddThemeColorOverride("font_color", tracked ? UiTheme.Accent : UiTheme.Dim);

        // Never rebuild inside a button signal (CLAUDE.md §8 / UiPanel) — flag it and let the
        // panel's own dirty loop redraw on the next frame.
        button.Pressed += () =>
        {
            _log?.Track(progress.Quest.Id);
            MarkDirty();
        };

        return button;
    }

    /// <summary>One active quest: title on a coloured spine, then an objective row per goal with a
    /// progress bar for anything counting past one.</summary>
    private Control BuildQuestCard(QuestProgress progress, Color tint)
    {
        PanelContainer card = UiTheme.Card(tint);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        // Title row: the name, and the control that chooses which quest the HUD follows (39.5B).
        //
        // ⚠️ This is the ONLY caller of QuestLogComponent.Track, and it exists because a tracked-quest
        // field with no way to set it is exactly the CraftingComponent.Learn failure the working
        // agreement names. It is reachable by keyboard and gamepad through the focus navigation
        // UiPanel already grabs on open — NOT by mouse, because the journal is non-modal and so leaves
        // the cursor captured by the player controller. That is the journal's existing contract, not
        // something introduced here, and making it modal would be redesigning a screen this sub-phase
        // is scoped out of touching.
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label title = UiTheme.Body(Loc.T(progress.Quest.Title), tint);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(title);
        titleRow.AddChild(TrackButton(progress));
        col.AddChild(titleRow);

        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveResource objective = objectives[i];

            // ⚠️ 41D. An objective on the branch the player did not take is not drawn here at all —
            // the journal is where a quest's shape is read, and listing an unreachable step is worse
            // than listing nothing. A LOCKED one (SequentialObjectives, earlier step outstanding) is
            // drawn dim and padlocked, because a quest whose rows appear one at a time reads as the
            // journal losing them.
            if (!progress.IsObjectiveInBranch(i))
            {
                continue;
            }

            bool locked = !progress.IsObjectiveActive(i);
            bool done = progress.IsObjectiveComplete(i);
            int have = progress.Counts[i];
            int required = Mathf.Max(1, objective.RequiredCount);

            col.AddChild(UiTheme.Caption(
                $"{(done ? "✓" : locked ? "🔒" : "•")} {Loc.T(objective.ShortLabel())}  {have}/{objective.RequiredCount}",
                done ? UiTheme.QuestComplete : locked ? UiTheme.Dim : UiTheme.Text));

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
