using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The quest journal: a modal, fully interactive list/detail workspace. It owns focus and the mouse
/// while open so track/untrack is equally reachable by mouse, keyboard and controller.
///
/// The index is a set of section tabs (Main Thread, Errands, Completed, Failed - each present only with
/// state in it) stepped with Q/E or LB/RB. The Main tab groups quests under collapsible chapter headings.
/// The detail pane reads like a journal: who gave it, what has been done (the stage log, each line ticked),
/// what to do now with its hint and where to go, optional steps as chips, and the full rewards. All the
/// ordering and selection decisions live in Godot-free rules (<see cref="JournalIndexRules"/>,
/// <see cref="StageLogRules"/>) so they are tested without an engine.
/// </summary>
public partial class QuestLogPanel : UiPanel
{
    private QuestLogComponent? _log;
    private VBoxContainer _index = null!;
    private HFlowContainer _tabs = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private Label _footer = null!;

    private string? _selectedId;
    private JournalSection _section = JournalSection.Main;
    private readonly HashSet<string> _collapsedChapters = new();
    private bool _ledgerOpen;
    private bool _showAllCompleted;

    protected override string? ToggleAction => GameInput.Journal;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        MarginContainer margin = UiTheme.Padding(UiTheme.SpaceLg);
        shell.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        root.AddChild(UiTheme.Title(Loc.T("questui.journal_title")));
        root.AddChild(UiTheme.Divider());
        margin.AddChild(root);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        root.AddChild(body);

        // The index is bare ground, not a Well: its rows are Cards, and a recess around raised rows is two
        // frames for one list.
        _index = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _index.CustomMinimumSize = new Vector2(340f, 0f);
        _index.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        body.AddChild(_index);

        _tabs = new HFlowContainer();
        _tabs.AddThemeConstantOverride("h_separation", UiTheme.SpaceSm);
        _tabs.AddThemeConstantOverride("v_separation", UiTheme.SpaceSm);
        _index.AddChild(_tabs);

        (ScrollContainer indexScroll, VBoxContainer indexList) = UiTheme.ScrollList();
        _list = indexList;
        _index.AddChild(indexScroll);

        // The Band is the detail's frame and already carries card padding, so the scroll goes straight in.
        PanelContainer detailBand = UiTheme.Band(UiTheme.QuestMain);
        detailBand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailBand.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(detailBand);
        (ScrollContainer detailScroll, VBoxContainer detailList) = UiTheme.ScrollList();
        _detail = detailList;
        _detail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        detailBand.AddChild(detailScroll);

        _footer = UiTheme.Caption(string.Empty, UiTheme.Dim);
        root.AddChild(_footer);
    }

    protected override void OnReady()
    {
        EventBus bus = EventBus.Instance;
        bus?.Subscribe<QuestStartedEvent>(OnQuestEvent);
        bus?.Subscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);

        // ⚠️ CAUGHT BY A RENDERED FRAME, NOT BY REVIEW (41B). Without this line the journal keeps
        // showing a failed quest under ERRANDS, still labelled TRACKED, until some other quest event
        // happens to mark the panel dirty - while the toast says it failed and the HUD tracker has
        // already moved on. Three surfaces, two answers. A new state has to reach every surface that
        // draws the old one.
        bus?.Subscribe<QuestFailedEvent>(OnQuestFailed);
        bus?.Subscribe<QuestResetEvent>(OnQuestReset);
        bus?.Subscribe<GameLoadedEvent>(OnGameLoaded);

        // ⚠️ 41D, and this line exists because 41B shipped its absence. A quest's BRANCH changes on a
        // story flag, which is not a quest event at all - so a fork chosen while the journal is open
        // would leave the card showing the path the player just declined, until some unrelated quest
        // event happened to rebuild it. 41B's rule, one sub-phase later: the grep is not "who draws
        // quests" but "what else can change what a quest looks like".
        bus?.Subscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);

        // The campaign model adds three more things that change a card: a step opening or closing, the
        // updated dot being cleared, and the prompts changing with the input device.
        bus?.Subscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        bus?.Subscribe<QuestStageChangedEvent>(OnStageChanged);
        bus?.Subscribe<QuestSeenChangedEvent>(OnSeenChanged);
        bus?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        EventBus bus = EventBus.Instance;
        if (bus == null)
        {
            return;
        }

        bus.Unsubscribe<QuestStartedEvent>(OnQuestEvent);
        bus.Unsubscribe<QuestObjectiveAdvancedEvent>(OnObjectiveAdvanced);
        bus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
        bus.Unsubscribe<QuestResetEvent>(OnQuestReset);
        bus.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        bus.Unsubscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
        bus.Unsubscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        bus.Unsubscribe<QuestStageChangedEvent>(OnStageChanged);
        bus.Unsubscribe<QuestSeenChangedEvent>(OnSeenChanged);
        bus.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
    }

    public void SetQuestLog(QuestLogComponent? log)
    {
        _log = log;
        MarkDirty();
    }

    /// <summary>The quest whose card is open, as of the last rebuild. Read by the screenshot harness to prove it
    /// photographed the card it meant to.</summary>
    public string? SelectedQuestId => _selectedId;

    /// <summary>The index tab that is open, as of the last rebuild.</summary>
    public JournalSection CurrentSection => _section;

    /// <summary>Selects a quest by id and opens its section, so a harness (or a future deep link from a
    /// toast) can land on a specific card through the panel's own path.</summary>
    public void Select(string questId)
    {
        _selectedId = questId;
        MarkDirty();
    }

    private void OnQuestEvent(QuestStartedEvent e) => MarkDirty();

    private void OnObjectiveAdvanced(QuestObjectiveAdvancedEvent e) => MarkDirty();

    private void OnQuestCompleted(QuestCompletedEvent e) => MarkDirty();

    private void OnQuestFailed(QuestFailedEvent e) => MarkDirty();

    private void OnQuestReset(QuestResetEvent e) => MarkDirty();

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    private void OnStoryFlagChanged(Dialogue.StoryFlagChangedEvent e) => MarkDirty();

    private void OnObjectiveActivated(QuestObjectiveActivatedEvent e) => MarkDirty();

    private void OnStageChanged(QuestStageChangedEvent e) => MarkDirty();

    private void OnSeenChanged(QuestSeenChangedEvent e) => MarkDirty();

    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

    // --- Input ---------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        int step = SectionStep(@event);
        if (step != 0)
        {
            StepSection(step);
            GetViewport().SetInputAsHandled();
            return;
        }

        // The pad's track toggle rides the Interact button. On a keyboard Interact is E, which is already the
        // next-section key, so the keyboard reaches the button by mouse or Enter.
        if (@event is InputEventJoypadButton { Pressed: true } pad && pad.IsAction(GameInput.Interact))
        {
            ToggleTrackSelected();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>-1 for Q / LB, +1 for E / RB, else 0.</summary>
    private static int SectionStep(InputEvent @event) => @event switch
    {
        InputEventKey { Pressed: true, Echo: false } key =>
            (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode) switch
            {
                Key.Q => -1,
                Key.E => 1,
                _ => 0,
            },
        InputEventJoypadButton { Pressed: true } pad => pad.ButtonIndex switch
        {
            JoyButton.LeftShoulder => -1,
            JoyButton.RightShoulder => 1,
            _ => 0,
        },
        _ => 0,
    };

    private void StepSection(int delta)
    {
        if (_log == null)
        {
            return;
        }

        List<JournalEntry> entries = Entries(out _);
        List<JournalSection> sections = JournalIndexRules.Sections(entries);
        if (sections.Count < 2)
        {
            return;
        }

        SwitchSection(JournalIndexRules.Step(sections, _section, delta), entries);
    }

    private void SwitchSection(JournalSection section, List<JournalEntry> entries)
    {
        _section = section;
        _selectedId = FirstOf(section, entries);
        MarkDirty();
    }

    private void ToggleTrackSelected()
    {
        if (_log == null || _selectedId == null || !_log.IsActive(_selectedId) ||
            Find(_selectedId) is not { } progress || progress.Quest.IsLedger)
        {
            return;
        }

        bool tracked = ReferenceEquals(_log.Tracked, progress);
        _log.Track(tracked ? null : progress.Quest.Id);
        MarkDirty();
    }

    // --- Model ---------------------------------------------------------------------------

    private List<JournalEntry> Entries(out Dictionary<string, QuestProgress> byId)
    {
        byId = new Dictionary<string, QuestProgress>();
        var entries = new List<JournalEntry>();
        if (_log == null)
        {
            return entries;
        }

        int sequence = 0;
        foreach (QuestProgress progress in _log.Quests)
        {
            QuestResource quest = progress.Quest;
            entries.Add(new JournalEntry(
                quest.Id, progress.Status, quest.IsMainQuest, quest.IsLedger, quest.ChapterKey,
                quest.OrderInAct, sequence++));
            byId[quest.Id] = progress;
        }

        return entries;
    }

    private QuestProgress? Find(string id)
    {
        if (_log == null)
        {
            return null;
        }

        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Quest.Id == id)
            {
                return progress;
            }
        }

        return null;
    }

    /// <summary>The quest a section opens on: the tracked one when it lives there, else the first listed.</summary>
    private string? FirstOf(JournalSection section, List<JournalEntry> entries)
    {
        List<JournalEntry> listed = Listed(section, entries);
        if (listed.Count == 0)
        {
            return null;
        }

        string trackedId = _log?.Tracked?.Quest.Id ?? string.Empty;
        foreach (JournalEntry entry in listed)
        {
            if (entry.Id == trackedId)
            {
                return entry.Id;
            }
        }

        return listed[0].Id;
    }

    private List<JournalEntry> Listed(JournalSection section, List<JournalEntry> entries)
    {
        var listed = new List<JournalEntry>();
        switch (section)
        {
            case JournalSection.Main:
                foreach (JournalGroup group in JournalIndexRules.MainGroups(entries))
                {
                    listed.AddRange(group.Entries);
                }

                listed.AddRange(JournalIndexRules.Ledger(entries));
                break;
            case JournalSection.Errands:
                listed.AddRange(JournalIndexRules.Errands(entries));
                break;
            case JournalSection.Completed:
                listed.AddRange(JournalIndexRules.Completed(entries, _showAllCompleted, out _));
                break;
            default:
                listed.AddRange(JournalIndexRules.Failed(entries));
                break;
        }

        return listed;
    }

    // --- Rebuild -------------------------------------------------------------------------

    protected override void Rebuild()
    {
        // Called here as well as in BuildShell: the UI-scale setting can change mid-session.
        UiTheme.ApplyScreenInset(Shell);
        _index.CustomMinimumSize = new Vector2(Mathf.Clamp(UiTheme.UsableWidth(Shell) * 0.32f, 240f, 360f), 0f);

        UiTheme.ClearChildren(_tabs);
        UiTheme.ClearChildren(_list);
        UiTheme.ClearChildren(_detail);
        _footer.Text = FooterText();

        if (_log == null || _log.Quests.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("questlog.none"), UiTheme.Dim));
            _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Quest, Loc.T("questlog.none"), tint: UiTheme.Dim));
            return;
        }

        List<JournalEntry> entries = Entries(out Dictionary<string, QuestProgress> byId);
        string? trackedId = _log.Tracked?.Quest.Id;

        // The selection decides the tab, so a quest that completes while the journal is open carries the
        // player to the Completed tab with it instead of vanishing from under them.
        if (_selectedId == null || !byId.ContainsKey(_selectedId))
        {
            JournalSection? start = JournalIndexRules.DefaultSection(entries, null, trackedId);
            _section = start ?? JournalSection.Main;
            _selectedId = FirstOf(_section, entries);
        }
        else
        {
            _section = JournalIndexRules.SectionOf(entries.Find(e => e.Id == _selectedId));
        }

        QuestProgress? selected = _selectedId != null && byId.TryGetValue(_selectedId, out QuestProgress? found) ? found : null;

        // Looking at a quest is reading its news: clear the dot before the rows are drawn, so the open
        // card's row does not draw a dot beside the card it is already showing.
        if (selected != null)
        {
            _log.MarkSeen(selected.Quest.Id);
        }

        List<JournalSection> sections = JournalIndexRules.Sections(entries);
        BuildTabs(sections, entries);
        BuildIndex(_section, entries, byId, trackedId);

        if (selected != null)
        {
            BuildDetail(selected);
        }
    }

    private string FooterText()
    {
        string sections = InputDevice.GamepadActive
            ? $"{GameInput.ButtonLabel(JoyButton.LeftShoulder)} / {GameInput.ButtonLabel(JoyButton.RightShoulder)}"
            : "Q / E";
        return Loc.TF("questui.footer", sections, GameInput.PromptLabel(GameInput.Journal));
    }

    private void BuildTabs(List<JournalSection> sections, List<JournalEntry> entries)
    {
        foreach (JournalSection section in sections)
        {
            JournalSection captured = section;
            int count = CountIn(section, entries);
            bool active = section == _section;

            Button tab = UiTheme.Action(Loc.TF("questui.tab", Loc.T(SectionKey(section)), count));
            tab.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
            tab.AddThemeColorOverride("font_color", active ? UiTheme.Accent : UiTheme.Dim);
            var box = new StyleBoxFlat
            {
                BgColor = active ? UiTheme.CardBg : new Color(0f, 0f, 0f, 0f),
                BorderColor = UiTheme.Accent,
            };
            box.SetBorderWidthAll(0);
            box.BorderWidthBottom = active ? 2 : 0;
            box.SetContentMarginAll(UiTheme.SpaceXs);
            box.ContentMarginLeft = UiTheme.SpaceMd;
            box.ContentMarginRight = UiTheme.SpaceMd;
            box.SetCornerRadiusAll(UiTheme.RadiusSm);
            tab.AddThemeStyleboxOverride("normal", box);

            // A text marker on a tab holding unread news, so the state is never carried by colour alone.
            if (section != _section && AnyUpdated(section, entries))
            {
                tab.Text += " *";
            }

            tab.Pressed += () =>
            {
                if (captured != _section)
                {
                    SwitchSection(captured, entries);
                }
            };
            _tabs.AddChild(tab);
        }
    }

    private static string SectionKey(JournalSection section) => section switch
    {
        JournalSection.Main => "questui.section.main",
        JournalSection.Errands => "questui.section.errands",
        JournalSection.Completed => "questui.section.completed",
        _ => "questui.section.failed",
    };

    private int CountIn(JournalSection section, List<JournalEntry> entries)
    {
        int count = 0;
        foreach (JournalEntry entry in entries)
        {
            if (JournalIndexRules.SectionOf(entry) == section)
            {
                count++;
            }
        }

        return count;
    }

    private bool AnyUpdated(JournalSection section, List<JournalEntry> entries)
    {
        foreach (JournalEntry entry in entries)
        {
            if (JournalIndexRules.SectionOf(entry) == section && _log!.IsUpdated(entry.Id))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildIndex(
        JournalSection section, List<JournalEntry> entries, Dictionary<string, QuestProgress> byId, string? trackedId)
    {
        switch (section)
        {
            case JournalSection.Main:
                BuildMain(entries, byId, trackedId);
                break;

            case JournalSection.Errands:
                foreach (JournalEntry entry in JournalIndexRules.Errands(entries))
                {
                    _list.AddChild(QuestRow(byId[entry.Id], UiTheme.QuestSide, trackedId));
                }

                break;

            case JournalSection.Completed:
                BuildCompleted(entries, byId, trackedId);
                break;

            default:
                // The failed tab states the outcome in words as well as the red spine (UI_STYLE §2: colour is
                // never the only channel), and says nothing about a second attempt, which belongs to the
                // giver's conversation.
                foreach (JournalEntry entry in JournalIndexRules.Failed(entries))
                {
                    _list.AddChild(QuestRow(byId[entry.Id], UiTheme.QuestFailed, trackedId));
                }

                break;
        }
    }

    private void BuildMain(List<JournalEntry> entries, Dictionary<string, QuestProgress> byId, string? trackedId)
    {
        List<JournalGroup> groups = JournalIndexRules.MainGroups(entries);
        bool headings = JournalIndexRules.NeedsChapterHeadings(groups);

        foreach (JournalGroup group in groups)
        {
            bool open = true;
            if (headings)
            {
                string key = group.ChapterKey;
                open = !_collapsedChapters.Contains(key);
                _list.AddChild(ChapterHeader(ChapterTitle(key), group.Entries.Count, open, () =>
                {
                    if (!_collapsedChapters.Remove(key))
                    {
                        _collapsedChapters.Add(key);
                    }

                    MarkDirty();
                }));
            }

            if (!open)
            {
                continue;
            }

            foreach (JournalEntry entry in group.Entries)
            {
                _list.AddChild(QuestRow(byId[entry.Id], UiTheme.QuestMain, trackedId));
            }
        }

        // Ledger quests are umbrella records: listed, never tracked, folded away until asked for.
        List<JournalEntry> ledger = JournalIndexRules.Ledger(entries);
        if (ledger.Count == 0)
        {
            return;
        }

        bool ledgerOpen = _ledgerOpen || (_selectedId != null && ledger.Exists(e => e.Id == _selectedId));
        _list.AddChild(ChapterHeader(Loc.T("questui.ledger"), ledger.Count, ledgerOpen, () =>
        {
            _ledgerOpen = !_ledgerOpen;
            MarkDirty();
        }));

        if (ledgerOpen)
        {
            foreach (JournalEntry entry in ledger)
            {
                _list.AddChild(QuestRow(byId[entry.Id], UiTheme.Dim, trackedId));
            }
        }
    }

    private void BuildCompleted(List<JournalEntry> entries, Dictionary<string, QuestProgress> byId, string? trackedId)
    {
        List<JournalEntry> shown = JournalIndexRules.Completed(entries, _showAllCompleted, out int hidden);
        foreach (JournalEntry entry in shown)
        {
            _list.AddChild(QuestRow(byId[entry.Id], UiTheme.QuestComplete, trackedId));
        }

        if (hidden > 0 || _showAllCompleted)
        {
            Button more = UiTheme.Action(_showAllCompleted
                ? Loc.T("questui.completed_fewer")
                : Loc.TF("questui.completed_more", hidden));
            more.AddThemeColorOverride("font_color", UiTheme.Dim);
            more.Pressed += () =>
            {
                _showAllCompleted = !_showAllCompleted;
                MarkDirty();
            };
            _list.AddChild(more);
        }
    }

    /// <summary>The heading for a chapter, from its locale keys (main set, then the secret set). A chapter
    /// whose text is missing reads as the generic main thread rather than showing a raw key.</summary>
    private static string ChapterTitle(string chapterKey)
    {
        string? key = JournalIndexRules.FirstResolving(JournalIndexRules.ChapterTitleKeys(chapterKey), Loc.Has);
        return key != null ? Loc.T(key) : Loc.T("questui.main_thread");
    }

    private static Control ChapterHeader(string title, int count, bool open, System.Action onToggle)
    {
        Button header = UiTheme.Action(Loc.TF(open ? "questui.chapter_open" : "questui.chapter_closed", title, count));
        header.AddThemeColorOverride("font_color", UiTheme.Accent);
                header.Pressed += onToggle;
        return header;
    }

    private Control QuestRow(QuestProgress progress, Color tint, string? trackedId)
    {
        QuestResource quest = progress.Quest;
        bool selected = quest.Id == _selectedId;
        bool updated = _log!.IsUpdated(quest.Id);

        PanelContainer card = UiTheme.CardButton(tint, out Button input, out VBoxContainer content);
        if (selected)
        {
            StyleBoxFlat style = UiTheme.CardStyle(tint);
            style.BgColor = UiTheme.CardBg with { A = 1f };
            style.BorderWidthLeft = 4;
            card.AddThemeStyleboxOverride("panel", style);
        }

        var titleRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Label title = UiTheme.Body(Loc.T(quest.Title), selected ? tint : UiTheme.Text);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        titleRow.AddChild(title);

        if (updated)
        {
            // A shape rather than a glyph, so it needs no font and survives every colour setting; the tooltip
            // and the tab asterisk say the same thing in words.
            var dot = new ColorRect
            {
                Color = UiTheme.Adapt(UiTheme.Accent),
                CustomMinimumSize = new Vector2(9f, 9f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            titleRow.AddChild(dot);
            input.TooltipText = Loc.T("questui.updated_tip");
        }

        content.AddChild(titleRow);

        string? note = progress.Status switch
        {
            QuestStatus.Completed => Loc.T("questui.status.completed"),
            QuestStatus.Failed => Loc.T("questui.status.failed"),
            _ when quest.Id == trackedId => Loc.T("questui.status.tracked"),
            _ => null,
        };
        if (updated)
        {
            note = note == null ? Loc.T("questui.status.updated") : $"{note}  {Loc.T("questui.status.updated")}";
        }

        if (note != null)
        {
            Label caption = UiTheme.Caption(note, UiTheme.Dim);
            caption.MouseFilter = Control.MouseFilterEnum.Ignore;
            content.AddChild(caption);
        }

        input.Pressed += () =>
        {
            _selectedId = quest.Id;
            MarkDirty();
        };
        return card;
    }

    // --- Detail --------------------------------------------------------------------------

    private void BuildDetail(QuestProgress progress)
    {
        QuestResource quest = progress.Quest;
        Color tint = progress.Status == QuestStatus.Completed ? UiTheme.QuestComplete
            : progress.Status == QuestStatus.Failed ? UiTheme.QuestFailed
            : quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide;

        _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Quest, Loc.T(quest.Title), tint: tint));
        _detail.AddChild(DetailChips(progress));

        if (quest.GiverNameKey.Length > 0 && Loc.Has(quest.GiverNameKey))
        {
            _detail.AddChild(UiTheme.Caption(Loc.TF("questui.giver", Loc.T(quest.GiverNameKey)), UiTheme.Dim));
        }

        // Long prose when authored, the one-line summary otherwise, and nothing when neither resolves.
        string? prose = JournalIndexRules.FirstResolving(
            new[] { quest.DetailKey, quest.Summary }, key => key.Length > 0 && Loc.Has(key));
        if (prose != null)
        {
            Label text = UiTheme.Prose(Loc.T(prose), UiTheme.Text);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detail.AddChild(text);
        }
        else if (quest.Summary.Length > 0)
        {
            _detail.AddChild(UiTheme.Prose(Loc.T(quest.Summary), UiTheme.Text));
        }

        if (progress.Status == QuestStatus.Active && !quest.IsLedger)
        {
            _detail.AddChild(TrackButton(progress));
        }

        if (progress.IsTimed && progress.Status == QuestStatus.Active)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(progress.SecondsLeft));
            _detail.AddChild(UiTheme.IconLabel(
                UiIcon.Kind.Warning,
                Loc.TF("hud.quest.time_left", seconds / 60, (seconds % 60).ToString("00")),
                tint: seconds <= 10 ? UiTheme.AccentHot : UiTheme.Dim));
        }

        BuildStageLog(progress, tint);
        BuildRewards(quest);
    }

    private Control DetailChips(QuestProgress progress)
    {
        QuestResource quest = progress.Quest;
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        chips.AddThemeConstantOverride("v_separation", UiTheme.ChipGap);

        chips.AddChild(UiTheme.Chip(
            Loc.T(quest.IsLedger ? "questui.chip.ledger" : quest.IsMainQuest ? "questui.chip.main" : "questui.chip.errand"),
            quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide));

        if (quest.ChapterKey.Length > 0 &&
            JournalIndexRules.FirstResolving(JournalIndexRules.ChapterTitleKeys(quest.ChapterKey), Loc.Has) is { } chapter)
        {
            chips.AddChild(UiTheme.Chip(Loc.T(chapter), UiTheme.Dim));
        }

        string region = QuestPlaces.RegionName(quest.RegionId);
        if (region.Length > 0)
        {
            chips.AddChild(UiTheme.Chip(region, UiTheme.Text));
        }

        if (quest.RecommendedLevel > 0)
        {
            chips.AddChild(UiTheme.Chip(Loc.TF("questui.chip.level", quest.RecommendedLevel), UiTheme.Text));
        }

        if (progress.Status == QuestStatus.Completed)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("questui.status.completed"), UiTheme.QuestComplete));
        }
        else if (progress.Status == QuestStatus.Failed)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("questui.status.failed"), UiTheme.QuestFailed));
        }

        return chips;
    }

    private void BuildStageLog(QuestProgress progress, Color tint)
    {
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        List<StageLine> lines = StageLogRules.Build(QuestProgressViews.States(progress), progress.Status == QuestStatus.Active);
        if (lines.Count == 0)
        {
            return;
        }

        _detail.AddChild(UiTheme.SectionRule(Loc.T("questui.stage_header")));
        foreach (StageLine line in lines)
        {
            _detail.AddChild(StageBand(progress, objectives[line.Index], line, tint));
        }
    }

    private Control StageBand(QuestProgress progress, ObjectiveResource objective, StageLine line, Color tint)
    {
        int have = progress.Counts[line.Index];
        int required = Mathf.Max(1, objective.RequiredCount);
        bool done = line.Kind is StageKind.Done or StageKind.OptionalDone;
        bool live = line.Kind is StageKind.Current or StageKind.Optional;

        Color edge = done ? UiTheme.QuestComplete : live ? tint : UiTheme.Dim;
        PanelContainer band = UiTheme.Band(edge);
        var copy = new VBoxContainer();
        copy.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        string textKey = done
            ? StageLogRules.LogTextKey(objective.JournalEntryKey, objective.ShortLabel(), Loc.Has)
            : objective.ShortLabel();
        string text = Loc.T(textKey);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        if (done)
        {
            // A text tick, so a finished step is told apart from a pending one without colour.
            Label tick = UiTheme.Body(Loc.TF("questui.tick_line", text), UiTheme.QuestComplete);
            tick.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            tick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(tick);
        }
        else
        {
            row.AddChild(UiIcon.Create(
                line.Kind is StageKind.Locked or StageKind.Missed ? UiIcon.Kind.Lock : UiIcon.Kind.Waypoint,
                20f,
                live ? UiTheme.Text : UiTheme.Dim));
            Label label = UiTheme.Body(text, live ? UiTheme.Text : UiTheme.Dim);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);
            if (objective.RequiredCount > 1)
            {
                row.AddChild(UiTheme.Caption($"{have}/{objective.RequiredCount}", UiTheme.Dim));
            }
        }

        if (objective.IsOptional)
        {
            row.AddChild(UiTheme.Chip(Loc.T("questui.chip.optional"), UiTheme.Dim));
        }

        copy.AddChild(row);

        if (live && objective.RequiredCount > 1)
        {
            ProgressBar bar = UiTheme.Bar(tint, 360f);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.Value = Mathf.Clamp(have / (double)required, 0d, 1d);
            copy.AddChild(bar);
        }

        if (StageLogRules.ShowsHint(line.Kind, objective.HintKey, Loc.Has))
        {
            copy.AddChild(UiTheme.Flavour(Loc.T(objective.HintKey), UiTheme.Dim));
        }

        // Where to go: the objective's place by the shared navigation rule (Reach and Defend name it in
        // TargetId), with its realm when that is known.
        if (live && QuestPlaces.PlaceName(objective) is { } place)
        {
            string region = QuestPlaces.RegionName(QuestPlaces.RegionIdOfLocation(QuestPlaces.PlaceId(objective)));
            copy.AddChild(UiTheme.Caption(
                region.Length > 0 ? Loc.TF("questui.where_in", place, region) : Loc.TF("questui.where", place),
                UiTheme.Accent));
        }

        band.AddChild(copy);
        return band;
    }

    private void BuildRewards(QuestResource quest)
    {
        bool hasItems = false;
        foreach (Variant element in quest.RewardItems)
        {
            if (element.As<QuestItemReward>() is { Quantity: > 0 })
            {
                hasItems = true;
            }
        }

        bool faction = quest.FactionRewardId.Length > 0 && quest.FactionRewardAmount != 0;
        if (quest.XpReward <= 0 && quest.GoldReward <= 0 && !hasItems && !faction)
        {
            return;
        }

        _detail.AddChild(UiTheme.SectionRule(Loc.T("questlog.rewards")));
        if (quest.XpReward > 0)
        {
            _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Spell,
                Loc.TF("questlog.reward_xp", quest.XpReward), tint: UiTheme.Accent));
        }

        if (quest.GoldReward > 0)
        {
            _detail.AddChild(UiTheme.IconLabel(UiIcon.Kind.Currency,
                Loc.TF("questui.reward_gold", quest.GoldReward), tint: UiTheme.Accent));
        }

        if (hasItems)
        {
            var items = new HFlowContainer();
            items.AddThemeConstantOverride("h_separation", UiTheme.SpaceMd);
            items.AddThemeConstantOverride("v_separation", UiTheme.SpaceSm);
            foreach (Variant element in quest.RewardItems)
            {
                if (element.As<QuestItemReward>() is { Quantity: > 0 } reward &&
                    ItemDatabase.Get(reward.ItemId) is { } item)
                {
                    items.AddChild(RewardItem(new ItemInstance(item), reward.Quantity));
                }
            }

            _detail.AddChild(items);
        }

        if (faction)
        {
            string name = FactionDatabase.Get(quest.FactionRewardId) is { } f ? Loc.T(f.DisplayName) : quest.FactionRewardId;
            Color color = UiTheme.ReputationColor(RewardRules.FactionTier(quest.FactionRewardAmount));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
            row.AddChild(UiIcon.Create(UiIcon.Kind.Settlement, 20f, color));
            row.AddChild(UiTheme.Body(
                Loc.TF("questui.reward_faction", name, RewardRules.Signed(quest.FactionRewardAmount)), color));
            _detail.AddChild(row);
        }
    }

    private static Control RewardItem(ItemInstance item, int quantity)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Button slot = ItemSlot.Build(item, quantity, size: 40f);
        slot.FocusMode = Control.FocusModeEnum.None;
        row.AddChild(slot);
        Label name = UiTheme.Body(Loc.T(item.DisplayName), UiTheme.RarityColor(item.Rarity));
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);
        return row;
    }

    /// <summary>
    /// Follow-this-quest toggle. Shows which quest the HUD is currently on, and pressing it moves the
    /// tracker and the compass marker together (they read one authority since 39.5B).
    ///
    /// The state is carried by the label as well as the colour - "TRACKED" versus "TRACK" - because
    /// colour is never the only channel (UI_STYLE §2, brief §40). On a pad the label also names the
    /// button that toggles it.
    /// </summary>
    private Button TrackButton(QuestProgress progress)
    {
        bool tracked = ReferenceEquals(_log?.Tracked, progress);

        string label = Loc.T(tracked ? "questlog.untrack" : "questlog.track");
        if (InputDevice.GamepadActive)
        {
            label = Loc.TF("questui.with_prompt", label, GameInput.PromptLabel(GameInput.Interact));
        }

        Button button = UiTheme.Action(label);
        button.TooltipText = Loc.T("questlog.track_tip");
        button.AddThemeColorOverride("font_color", tracked ? UiTheme.Accent : UiTheme.Text);

        // Never rebuild inside a button signal (CLAUDE.md §8 / UiPanel) - flag it and let the
        // panel's own dirty loop redraw on the next frame.
        button.Pressed += () =>
        {
            _log?.Track(tracked ? null : progress.Quest.Id);
            MarkDirty();
        };

        return button;
    }
}
