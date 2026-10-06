using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The conversation window. It is fully event-driven: a <see cref="DialogueComponent"/>
/// publishes a <see cref="DialogueStartedEvent"/> on interact, this panel builds a
/// <see cref="DialogueSession"/> and renders the current line plus condition-filtered
/// choice buttons. Picking a choice applies its effect, advances the session and
/// rebuilds; an ending choice (or "Leave" on a dead-end node) closes the window.
///
/// While open it is modal (on the 30.5F <see cref="UiPanel"/> framework) — like the
/// character screen it frees the mouse and blocks the player controller so a choice
/// never drives the character, and rebuilds ride the base's dirty-flag loop.
///
/// Campaign additions: each choice carries text chips naming what it will do (derived from its effect
/// enums by <see cref="DialogueConsequenceTags"/>), a line under the speaker says when this person is the
/// objective of the tracked quest, the number keys 1-9 pick a choice, and a history toggle replays the last
/// few lines of the conversation.
///
/// The 2026-10 pass made it a lower third: the speaker and the line on the left, the options on the
/// right, the world still visible above. A line writes itself out (<see cref="DialoguePaceRules"/>) and
/// its options arrive when it is finished; accept finishes a line that is still being written before it
/// can choose anything. None of that touches the conversation itself, which is a
/// <see cref="DialogueSession"/> the panel only draws: a run that drives sessions without the panel sees
/// no difference, and an unattended run (headless, a capture harness, a pinned user folder) gets the
/// whole line at once.
/// </summary>
public partial class DialoguePanel : UiPanel
{
    // A conversation ends through its choices (or "Leave"), never a cancel press — closing on
    // B/Esc would strand the session and skip the DialogueEndedEvent (30.5J).
    protected override bool CloseOnCancel => false;

    private static readonly StringName UiAccept = "ui_accept";
    private static readonly StringName HistoryAction = GameInput.MenuTabNext;

    private VBoxContainer _page = null!;
    private VBoxContainer _options = null!;
    private ScrollContainer _optionScroll = null!;
    private ColorRect _scrim = null!;

    // The line on screen, and whether it is still being written out.
    private Label? _line;
    private DialogueNode? _typedNode;
    private bool _typing;
    private Tween? _typeTween;
    private bool _typewriterForced;
    private static bool? _unattended;

    // Set when a line finishes, so the options that arrive with the next rebuild take focus.
    private bool _focusOptions;
    private Button? _firstOption;

    private DialogueSession? _session;
    private IEntity? _player;
    private DialogueResource? _dialogue;

    // The conversation so far, for the history toggle; reset with every conversation.
    private readonly DialogueBacklog _backlog = new();
    private bool _historyOpen;

    // The choices on screen, in the order the number keys address them (1 = first).
    private readonly List<System.Action> _choiceActions = new();

    // ics:save-ui. A conversation holds a save block for as long as its session is live: a save
    // taken between a choice and its consequence would restore a world the dialogue half-changed.
    private const string SaveBlockReason = "save.blocked.dialogue";
    private System.IDisposable? _saveBlock;

    private void ReleaseSaveBlock()
    {
        _saveBlock?.Dispose();
        _saveBlock = null;
    }

    /// <summary>Accept skips a line that is still writing itself and chooses once it has; the history
    /// toggle is named while there is something to replay.</summary>
    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            if (_session == null)
            {
                return System.Array.Empty<LegendEntry>();
            }

            if (_typing)
            {
                return new[] { new LegendEntry("ui_accept", Loc.T("kn.legend.skip")) };
            }

            var entries = new List<LegendEntry> { new("ui_accept", Loc.T("kn.legend.choose")) };
            if (_historyOpen || _backlog.Recent().Count > 0)
            {
                entries.Add(new LegendEntry(GameInput.MenuTabNext,
                    Loc.T(_historyOpen ? "questui.dialogue.history_hide" : "questui.dialogue.history")));
            }

            return entries;
        }
    }

    protected override void BuildShell(PanelContainer shell)
    {
        _scrim = UiTheme.Scrim(0.40f);
        _scrim.Visible = false;
        _scrim.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_scrim);
        MoveChild(_scrim, 0);

        // The same plate as the hub's screens: one lit edge along the top, no box.
        UiTheme.ApplyHubPlate(shell);
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.AnchorTop = 1f;
        shell.AnchorBottom = 1f;
        shell.GrowHorizontal = Control.GrowDirection.Both;
        shell.GrowVertical = Control.GrowDirection.Begin;

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        margin.AddChild(row);

        // Left: who is speaking and what they say. It scrolls, for the rare line longer than the window.
        (ScrollContainer pageScroll, VBoxContainer page) = UiTheme.ScrollList();
        _page = page;
        _page.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(pageScroll);

        row.AddChild(new ColorRect
        {
            Color = UiTheme.Rule,
            CustomMinimumSize = new Vector2(1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // Right: the options, one RowGap list. The window is sized for five of them
        // (DialoguePaceRules.VisibleOptions); a sixth scrolls, and the focused one is kept in view.
        (ScrollContainer optionScroll, VBoxContainer options) = UiTheme.ScrollList();
        optionScroll.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _optionScroll = optionScroll;
        _options = options;
        row.AddChild(optionScroll);

        LayoutShell();
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        GetViewport().SizeChanged += LayoutShell;
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        GetViewport().SizeChanged -= LayoutShell;
        ReleaseSaveBlock();
    }

    protected override void OnOpenChanged(bool open)
    {
        _scrim.Visible = open;
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

    /// <summary>
    /// The lower third. Height is what five option rows need and no more than 40% of the view, so the
    /// speaker's face stays on screen; a short viewport shows fewer rows and scrolls the rest. The
    /// window stops above the footer legend's row.
    /// </summary>
    private void LayoutShell()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        // Three quarters of the view, but never so narrow that the line's column cannot hold a sentence:
        // a handheld gives the window nearly its whole width.
        float width = Mathf.Min(Mathf.Clamp(viewport.X * 0.74f, 720f, 1040f), viewport.X - (UiTheme.SpaceLg * 2f));

        int rows = DialoguePaceRules.VisibleOptions;
        float fiveRows = (UiTheme.ControlHeight * rows) + (UiTheme.RowGap * (rows - 1)) + (UiTheme.PanelPad * 2f) + UiTheme.Space2xs;
        float height = Mathf.Clamp(viewport.Y * 0.40f, 230f, fiveRows);

        float foot = UiChromeRules.LegendHeight + (UiChromeRules.ChromeMargin * 2f);
        Shell.OffsetLeft = -width * 0.5f;
        Shell.OffsetRight = width * 0.5f;
        Shell.OffsetTop = -height - foot;
        Shell.OffsetBottom = -foot;
        _optionScroll.CustomMinimumSize = new Vector2(Mathf.Clamp(width * 0.40f, 260f, 400f), 0f);
    }

    private void OnDialogueStarted(DialogueStartedEvent e)
    {
        // Ignore overlapping conversations: finish the current one first.
        if (_session != null)
        {
            return;
        }

        _player = e.Player;
        _dialogue = e.Dialogue;
        _session = new DialogueSession(e.Dialogue, e.Player);
        _saveBlock ??= Embervale.Save.SaveManager.Instance?.PushSaveBlock(SaveBlockReason);
        _backlog.Clear();
        _historyOpen = false;
        _typedNode = null;
        _typing = false;

        // A conversation with no reachable start node closes immediately.
        if (_session.IsEnded)
        {
            Close();
            return;
        }

        SetOpen(true);
    }

    private void Choose(DialogueChoice choice)
    {
        if (_session == null)
        {
            return;
        }

        _backlog.AddChoice(Loc.T(choice.Text));
        _backlog.MarkChosen(ChoiceKey(choice));
        if (_session.Choose(choice))
        {
            Close();
        }
        else
        {
            MarkDirty();
        }
    }

    // --- Input ---------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || _session == null)
        {
            return;
        }

        int number = -1;
        Key code = Key.None;
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            number = code is >= Key.Key1 and <= Key.Key9 ? (int)(code - Key.Key1)
                : code is >= Key.Kp1 and <= Key.Kp9 ? (int)(code - Key.Kp1)
                : -1;
        }

        // A line still being written: the press that would have chosen finishes the line instead, and
        // chooses nothing. The options are not on screen yet, so there is nothing it could have meant.
        if (_typing)
        {
            if (number >= 0 || @event.IsActionPressed(UiAccept) ||
                @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                FinishLine();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (number >= 0 && number < _choiceActions.Count)
        {
            GetViewport().SetInputAsHandled();
            _choiceActions[number]();
            return;
        }

        // H as it has always been, and the action the legend shows (E, or RB on a pad).
        if (code == Key.H || @event.IsActionPressed(HistoryAction))
        {
            ToggleHistory();
            GetViewport().SetInputAsHandled();
        }
    }

    // --- Typewriter ----------------------------------------------------------------------

    /// <summary>
    /// Whether lines write themselves out. Never under reduced motion. Never in an unattended run
    /// either (headless, a capture harness, a pinned user folder): a harness that photographs a
    /// conversation half a second after opening it must get the conversation, and nothing that drives
    /// the game without a player should have to wait for prose. The capture hook turns it back on for
    /// the frames that photograph it.
    /// </summary>
    private bool TypewriterOn => UiTheme.MotionEnabled && (_typewriterForced || !Unattended());

    private static bool Unattended()
    {
        if (_unattended is { } known)
        {
            return known;
        }

        bool unattended = DisplayServer.GetName() == "headless" || OS.GetEnvironment("EMBERVALE_USER_DIR").Length > 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            unattended |= arg.EndsWith("shots", System.StringComparison.Ordinal);
        }

        _unattended = unattended;
        return unattended;
    }

    /// <summary>Starts the line writing out. The label holds the whole text from the start (so it wraps
    /// and takes its height once) and only the count of visible characters moves.</summary>
    private void StartTyping(Label line)
    {
        _typeTween?.Kill();
        line.VisibleRatio = 0f;

        // Runs while the tree is paused (a conversation pauses the world) and ignores hit-stop.
        _typeTween = line.CreateTween();
        _typeTween.SetPauseMode(Tween.TweenPauseMode.Process);
        _typeTween.SetIgnoreTimeScale(true);
        _typeTween.TweenProperty(line, "visible_ratio", 1f, DialoguePaceRules.Seconds(line.Text.Length));
        _typeTween.TweenCallback(Callable.From(FinishLine));
    }

    /// <summary>Shows the whole line and brings the options in. Called when the line finishes on its
    /// own and when the player asks for it.</summary>
    private void FinishLine()
    {
        if (!_typing)
        {
            return;
        }

        _typing = false;
        _typeTween?.Kill();
        _typeTween = null;
        if (_line != null && IsInstanceValid(_line))
        {
            _line.VisibleRatio = 1f;
        }

        _focusOptions = true;
        MarkDirty();
    }

    /// <summary>Whether the line on screen is still writing itself out. Read by the screenshot harness.</summary>
    public bool IsTyping => _typing;

    /// <summary>How many options are on screen, as of the last rebuild.</summary>
    public int OptionCount => _choiceActions.Count;

    /// <summary>Turns the typewriter on for a capture harness, where it is otherwise off, so the next
    /// line opened can be photographed mid-write.</summary>
    public void TypewriterForCapture(bool on) => _typewriterForced = on;

    /// <summary>Finishes the line as an accept press does.</summary>
    public void FinishLineForCapture() => FinishLine();

    private void ToggleHistory()
    {
        // Nothing to show is nothing to toggle: it would draw an empty heading.
        if (!_historyOpen && _backlog.Recent().Count == 0)
        {
            return;
        }

        _historyOpen = !_historyOpen;
        MarkDirty();
    }

    /// <summary>
    /// Ends the open conversation exactly as picking a terminal choice does — the session is
    /// dropped, the panel closes and <c>DialogueEndedEvent</c> is published.
    ///
    /// ⚠️ It exists because <see cref="OnDialogueStarted"/> IGNORES an overlapping start, which is
    /// right for gameplay (two NPCs must not talk over each other) and is a trap for a harness: a
    /// second conversation opened over a live one silently shows the FIRST one's node under the
    /// second one's filename. <c>--guild-shots</c> photographs the same officer twice, as a stranger
    /// and as a member, and without this the member frame was the stranger frame.
    /// ⚠️ Not the same as <c>SetOpen(false)</c>, which hides the panel and leaves the session live.
    /// </summary>
    public void EndConversation() => Close();

    private void Close()
    {
        DialogueResource? dialogue = _dialogue;
        IEntity? player = _player;

        _session = null;
        _dialogue = null;
        _player = null;
        _typedNode = null;
        _typing = false;
        _typeTween?.Kill();
        _typeTween = null;
        _choiceActions.Clear();
        ReleaseSaveBlock();
        SetOpen(false);

        if (player != null && dialogue != null)
        {
            EventBus.Instance?.Publish(new DialogueEndedEvent(player, dialogue));
        }
    }

    // --- Rebuild -------------------------------------------------------------------------

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_page);
        UiTheme.ClearChildren(_options);
        _choiceActions.Clear();
        _line = null;
        _firstOption = null;

        if (_session?.CurrentNode is not { } node)
        {
            return;
        }

        string text = Loc.T(node.Text);
        _backlog.AddLine(text);

        // A node the panel has not shown yet starts writing; a rebuild of the one it is already
        // showing (history, a device change) settles it, because a line cannot resume part-written.
        if (!ReferenceEquals(node, _typedNode))
        {
            _typedNode = node;
            _typing = TypewriterOn && text.Length > 0;
        }
        else if (_typing)
        {
            _typing = false;
            _focusOptions = true;
        }

        // The illuminated page (37.5E): the speaker is carved, the words are set in the book serif,
        // and the choices are cards rather than a stack of buttons. Dialogue is the only screen in
        // the game the player *reads* rather than scans, so it is the one that most rewards the
        // typography split.
        _page.AddChild(UiTheme.Title(Loc.T(_session.CurrentSpeaker())));

        if (QuestContextLine() is { } context)
        {
            _page.AddChild(context);
        }

        if (_historyOpen)
        {
            _page.AddChild(HistoryBlock());
        }

        // The line is a size up from body text: it is the thing being read, and the column it sits in
        // holds it well inside the reading measure.
        Label line = UiTheme.Prose(text);
        UiTheme.ApplyType(line, UiTheme.FontRole.Serif, UiTheme.HeaderFontSize);
        line.VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping;
        _page.AddChild(line);
        _line = line;

        if (_typing)
        {
            // The options arrive with the end of the line.
            StartTyping(line);
            return;
        }

        BuildOptions();

        if (_focusOptions)
        {
            // After the base's own focus restore, which has nothing to restore to here.
            _focusOptions = false;
            _firstOption?.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private void BuildOptions()
    {
        List<DialogueChoice> choices = _session!.VisibleChoices();
        if (choices.Count == 0)
        {
            // Dead-end node: offer a single way out so the player is never stuck.
            AddOption(0, Loc.T("dialogue.leave"), UiTheme.Dim, OptionMark.Leave, null, Close);
            return;
        }

        for (int i = 0; i < choices.Count; i++)
        {
            DialogueChoice captured = choices[i];
            List<ConsequenceTag> tags = TagsFor(captured);
            bool ends = string.IsNullOrEmpty(captured.Goto);
            OptionMark mark = DialoguePaceRules.MarkOf(tags, _backlog.WasChosen(ChoiceKey(captured)), ends);

            // The spine agrees with the glyph: the story's own colour for a choice that moves it, quiet
            // for one already asked or one that only leaves.
            Color spine = mark switch
            {
                OptionMark.Plot => UiTheme.QuestMain,
                OptionMark.Exhausted or OptionMark.Leave => UiTheme.Dim,
                _ => UiTheme.Accent,
            };

            AddOption(i, Loc.T(captured.Text), spine, mark, tags, () => Choose(captured));
        }
    }

    /// <summary>What identifies a choice within one conversation: its words and where it leads.</summary>
    private static string ChoiceKey(DialogueChoice choice) => $"{choice.Text}>{choice.Goto}";

    /// <summary>The last few lines of this conversation and the choices taken between them, quietly.</summary>
    private Control HistoryBlock()
    {
        PanelContainer well = UiTheme.Well();
        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(UiTheme.Caption(Loc.T("questui.dialogue.history_title"), UiTheme.Accent));

        foreach (BacklogEntry entry in _backlog.Recent())
        {
            Label label = entry.Kind == BacklogKind.Choice
                ? UiTheme.Caption(Loc.TF("questui.dialogue.you", entry.Text), UiTheme.Accent)
                : UiTheme.Prose(entry.Text, UiTheme.Dim);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(label);
        }

        pad.AddChild(col);
        well.AddChild(pad);
        return well;
    }

    /// <summary>
    /// When this conversation is the objective of the tracked quest (a live Talk objective whose target is
    /// this dialogue), a line under the speaker says so and repeats the objective.
    /// </summary>
    private Control? QuestContextLine()
    {
        if (_dialogue == null ||
            _player?.GetComponent<QuestLogComponent>()?.Tracked is not { } progress)
        {
            return null;
        }

        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        List<ObjectiveState> states = QuestProgressViews.States(progress);
        var candidates = new List<DialogueQuestContext.TalkCandidate>(objectives.Count);

        // Required objectives are offered before optional ones, so the quest's own step wins a tie.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i].IsOptional == (pass == 1))
                {
                    candidates.Add(new DialogueQuestContext.TalkCandidate(
                        i, objectives[i].Type == ObjectiveType.Talk, objectives[i].TargetId,
                        ObjectiveFocusRules.IsLive(states[i])));
                }
            }
        }

        int index = DialogueQuestContext.Find(_dialogue.Id, candidates);
        if (index < 0)
        {
            return null;
        }

        Color tint = progress.Quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(UiIcon.Create(UiIcon.Kind.Quest, 16f, tint));
        Label label = UiTheme.Caption(
            Loc.TF("questui.dialogue.context", Loc.T(objectives[index].ShortLabel())), tint);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        return row;
    }

    // --- Consequence tags ----------------------------------------------------------------

    /// <summary>The chips for a choice: both of its effects and the on-enter effect of the node it leads to
    /// (taking the choice is what triggers it).</summary>
    private List<ConsequenceTag> TagsFor(DialogueChoice choice)
    {
        int onEnter = 0;
        string onEnterArg = string.Empty;
        if (choice.Goto.Length > 0 && _dialogue?.FindNode(choice.Goto) is { } target)
        {
            onEnter = (int)target.OnEnterEffect;
            onEnterArg = target.OnEnterEffectArg;
        }

        return DialogueConsequenceTags.ForChoice(
            (int)choice.Effect, choice.EffectArg,
            (int)choice.Effect2, choice.Effect2Arg,
            onEnter, onEnterArg);
    }

    private static (string Text, Color Color) TagLook(ConsequenceTag tag)
    {
        string signed = RewardRules.Signed(tag.Amount);
        switch (tag.Kind)
        {
            case ConsequenceKind.Quest:
                string title = QuestDatabase.Get(tag.Arg) is { } quest ? Loc.T(quest.Title) : string.Empty;
                return (title.Length > 0 ? Loc.TF("questui.tag.quest_named", title) : Loc.T("questui.tag.quest"),
                    UiTheme.QuestMain);

            case ConsequenceKind.Corruption:
                return (Loc.TF("questui.tag.corruption", signed), UiTheme.CorruptionText);

            case ConsequenceKind.Reputation:
                string faction = FactionDatabase.Get(tag.Arg) is { } f ? Loc.T(f.DisplayName) : tag.Arg;
                return (Loc.TF("questui.tag.reputation", faction, signed),
                    tag.Amount > 0 ? UiTheme.Good : UiTheme.Bad);

            case ConsequenceKind.Loyalty:
                return (Loc.TF("questui.tag.loyalty", CompanionName(tag.Arg), signed),
                    tag.Amount > 0 ? UiTheme.Good : UiTheme.Bad);

            case ConsequenceKind.Companion:
                return (Loc.TF(tag.Amount > 0 ? "questui.tag.companion_join" : "questui.tag.companion_leave",
                    CompanionName(tag.Arg)), tag.Amount > 0 ? UiTheme.Good : UiTheme.Dim);

            case ConsequenceKind.Guild:
                string guild = FactionDatabase.Get(tag.Arg) is { } g ? Loc.T(g.DisplayName) : tag.Arg;
                return (tag.Amount > 0
                    ? Loc.TF("questui.tag.guild_rank", guild, tag.Amount)
                    : Loc.TF("questui.tag.guild_join", guild), UiTheme.Accent);

            case ConsequenceKind.Item:
                string item = ItemDatabase.Get(tag.Arg) is { } resource ? Loc.T(resource.DisplayName) : tag.Arg;
                return (Loc.TF("questui.tag.item", signed, item), UiTheme.Text);

            default:
                return (Loc.T("questui.tag.story"), UiTheme.Dim);
        }
    }

    private static string CompanionName(string companionId) =>
        CompanionDatabase.Get(companionId) is { } companion ? Loc.T(companion.NameKey) : companionId;

    /// <summary>One dialogue option as an engraved card: the glyph for what it is, its number for the
    /// number keys, its words, and its consequence chips beneath. The whole card is the button, so the
    /// target is the full row rather than the text's own width, which also means the focus rule a gamepad
    /// follows matches what a mouse can click.</summary>
    private void AddOption(
        int index, string text, Color spine, OptionMark mark, List<ConsequenceTag>? tags, System.Action onPressed)
    {
        PanelContainer card = UiTheme.CardButton(
            spine, out Button input, out VBoxContainer content, UiTheme.Compact(UiTheme.CardStyle(spine)));
        card.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        content.Alignment = BoxContainer.AlignmentMode.Center;

        // The chips are a second line of the card, not part of the sentence: a full gap between them.
        content.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        line.AddChild(MarkOf(mark));

        Label words = UiTheme.Prose(
            index < 9 ? Loc.TF("questui.dialogue.choice_number", index + 1, text) : text,
            mark == OptionMark.Exhausted ? UiTheme.Dim : UiTheme.Text);
        words.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(words);
        content.AddChild(line);

        if (tags is { Count: > 0 })
        {
            var chips = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            chips.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
            chips.AddThemeConstantOverride("v_separation", UiTheme.ChipGap);
            foreach (ConsequenceTag tag in tags)
            {
                (string label, Color color) = TagLook(tag);
                chips.AddChild(UiTheme.Chip(label, color));
            }

            content.AddChild(chips);
        }

        // The glyph in words, for a pointer that rests on the option.
        input.TooltipText = mark switch
        {
            OptionMark.Plot => Loc.T("kn.dialogue.mark_plot"),
            OptionMark.Special => Loc.T("kn.dialogue.mark_special"),
            OptionMark.Exhausted => Loc.T("kn.dialogue.mark_exhausted"),
            OptionMark.Leave => Loc.T("kn.dialogue.mark_leave"),
            _ => string.Empty,
        };

        input.Pressed += () => onPressed();
        _choiceActions.Add(onPressed);
        _firstOption ??= input;
        _options.AddChild(card);
    }

    /// <summary>The glyph before an option. An unmarked option keeps the glyph's width, so every
    /// option's words start on one edge.</summary>
    private static Control MarkOf(OptionMark mark) => mark switch
    {
        OptionMark.Plot => new MarkGlyph(MarkKind.Diamond, UiTheme.Adapt(UiTheme.QuestMain)),
        OptionMark.Special => new MarkGlyph(MarkKind.DiamondHollow, UiTheme.Text),
        OptionMark.Exhausted => new MarkGlyph(MarkKind.Tick, UiTheme.Dim),
        OptionMark.Leave => new MarkGlyph(MarkKind.Dash, UiTheme.Dim),
        _ => new Control { CustomMinimumSize = new Vector2(16f, 16f), MouseFilter = Control.MouseFilterEnum.Ignore },
    };
}
