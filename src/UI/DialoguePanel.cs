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
/// </summary>
public partial class DialoguePanel : UiPanel
{
    // A conversation ends through its choices (or "Leave"), never a cancel press — closing on
    // B/Esc would strand the session and skip the DialogueEndedEvent (30.5J).
    protected override bool CloseOnCancel => false;

    private VBoxContainer _list = null!;
    private ColorRect _scrim = null!;

    private DialogueSession? _session;
    private IEntity? _player;
    private DialogueResource? _dialogue;

    // The conversation so far, for the history toggle; reset with every conversation.
    private readonly DialogueBacklog _backlog = new();
    private bool _historyOpen;

    // The choices on screen, in the order the number keys address them (1 = first).
    private readonly List<System.Action> _choiceActions = new();

    protected override void BuildShell(PanelContainer shell)
    {
        _scrim = UiTheme.Scrim(0.40f);
        _scrim.Visible = false;
        _scrim.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_scrim);
        MoveChild(_scrim, 0);

        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.AnchorTop = 1f;
        shell.AnchorBottom = 1f;
        shell.GrowHorizontal = Control.GrowDirection.Both;
        shell.GrowVertical = Control.GrowDirection.Begin;
        LayoutShell();

        MarginContainer margin = UiTheme.Padding(UiTheme.SpaceLg);
        shell.AddChild(margin);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddChild(scroll);

        _list = new VBoxContainer();
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        scroll.AddChild(_list);
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
    }

    protected override void OnOpenChanged(bool open)
    {
        _scrim.Visible = open;
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

    private void LayoutShell()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Clamp(viewport.X * 0.68f, 560f, 920f);

        // Taller than the original 42%: a choice now carries consequence chips and the speaker a quest
        // line, and a window that scrolls on the first conversation reads as clipped.
        float height = Mathf.Clamp(viewport.Y * 0.52f, 280f, 480f);
        ShellOrFallback().OffsetLeft = -width * 0.5f;
        ShellOrFallback().OffsetRight = width * 0.5f;
        ShellOrFallback().OffsetTop = -height - UiTheme.SpaceLg;
        ShellOrFallback().OffsetBottom = -UiTheme.SpaceLg;
    }

    private PanelContainer ShellOrFallback() => Shell;

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
        _backlog.Clear();
        _historyOpen = false;

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

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            int number = code is >= Key.Key1 and <= Key.Key9 ? (int)(code - Key.Key1)
                : code is >= Key.Kp1 and <= Key.Kp9 ? (int)(code - Key.Kp1)
                : -1;
            if (number >= 0 && number < _choiceActions.Count)
            {
                GetViewport().SetInputAsHandled();
                _choiceActions[number]();
                return;
            }

            if (code == Key.H)
            {
                ToggleHistory();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventJoypadButton { Pressed: true } pad && pad.ButtonIndex == JoyButton.RightShoulder)
        {
            ToggleHistory();
            GetViewport().SetInputAsHandled();
        }
    }

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

    /// <summary>The key or button that toggles the history view, as the player's current device would name it.</summary>
    private static string HistoryPrompt() =>
        InputDevice.GamepadActive ? GameInput.ButtonLabel(JoyButton.RightShoulder) : "H";

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
        _choiceActions.Clear();
        SetOpen(false);

        if (player != null && dialogue != null)
        {
            EventBus.Instance?.Publish(new DialogueEndedEvent(player, dialogue));
        }
    }

    // --- Rebuild -------------------------------------------------------------------------

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);
        _choiceActions.Clear();

        if (_session?.CurrentNode is not { } node)
        {
            return;
        }

        string text = Loc.T(node.Text);
        _backlog.AddLine(text);

        // The illuminated page (37.5E): the speaker is carved, the words are set in the book serif,
        // and the choices are cards rather than a stack of buttons. Dialogue is the only screen in
        // the game the player *reads* rather than scans, so it is the one that most rewards the
        // typography split -- and the one where a row of identical grey buttons most obviously
        // reads as a form.
        _list.AddChild(SpeakerRow(Loc.T(_session.CurrentSpeaker())));
        _list.AddChild(UiTheme.Divider());

        if (QuestContextLine() is { } context)
        {
            _list.AddChild(context);
        }

        if (_historyOpen)
        {
            _list.AddChild(HistoryBlock());
        }

        _list.AddChild(UiTheme.Prose(text));

        _list.AddChild(UiTheme.Divider());

        List<DialogueChoice> choices = _session.VisibleChoices();
        if (choices.Count == 0)
        {
            // Dead-end node: offer a single way out so the player is never stuck.
            AddChoiceCard(0, Loc.T("dialogue.leave"), UiTheme.Dim, null, Close);
            return;
        }

        for (int i = 0; i < choices.Count; i++)
        {
            DialogueChoice captured = choices[i];

            // A choice that starts a quest or ends the conversation is worth marking apart from
            // ordinary talk -- the spine is the cheapest way to say so without adding a legend.
            Color spine = captured.Effect == DialogueEffect.StartQuest ? UiTheme.QuestMain
                : string.IsNullOrEmpty(captured.Goto) ? UiTheme.Dim
                : UiTheme.Accent;

            AddChoiceCard(i, Loc.T(captured.Text), spine, TagsFor(captured), () => Choose(captured));
        }

        _list.Modulate = UiTheme.MotionEnabled ? new Color(1f, 1f, 1f, 0.28f) : Colors.White;
        UiTheme.AnimateModulate(_list, Colors.White, UiTheme.DurationBase);
    }

    /// <summary>The speaker's name, with the history prompt on the right when there is something to replay.</summary>
    private Control SpeakerRow(string speaker)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        Label name = UiTheme.Title(speaker);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);

        if (_historyOpen || _backlog.Recent().Count > 0)
        {
            row.AddChild(UiTheme.KeyCap(HistoryPrompt()));
            row.AddChild(UiTheme.Caption(
                Loc.T(_historyOpen ? "questui.dialogue.history_hide" : "questui.dialogue.history"), UiTheme.Dim));
        }

        return row;
    }

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

    /// <summary>The chips for a choice: both of its effects and the on-enter effect of the node it leads to.
    /// The second effect and the on-enter effect come from the story branch's dialogue extensions; they are
    /// read through the resource's property bag so this compiles with or without those fields.</summary>
    private List<ConsequenceTag> TagsFor(DialogueChoice choice)
    {
        int onEnter = 0;
        string onEnterArg = string.Empty;
        if (choice.Goto.Length > 0 && _dialogue?.FindNode(choice.Goto) is { } target)
        {
            onEnter = IntOf(target, "OnEnterEffect");
            onEnterArg = StringOf(target, "OnEnterEffectArg");
        }

        return DialogueConsequenceTags.ForChoice(
            (int)choice.Effect, choice.EffectArg,
            IntOf(choice, "Effect2"), StringOf(choice, "Effect2Arg"),
            onEnter, onEnterArg);
    }

    private static int IntOf(GodotObject resource, string property)
    {
        Variant value = resource.Get(property);
        return value.VariantType == Variant.Type.Int ? value.AsInt32() : 0;
    }

    private static string StringOf(GodotObject resource, string property)
    {
        Variant value = resource.Get(property);
        return value.VariantType == Variant.Type.String ? value.AsString() : string.Empty;
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

    /// <summary>One dialogue choice as an engraved card, numbered for the number keys, with its consequence
    /// chips beneath the words. The whole card is the button, so the target is the full row rather than the
    /// text's own width, which also means the focus rule a gamepad follows matches what a mouse can click.</summary>
    private void AddChoiceCard(int index, string text, Color spine, List<ConsequenceTag>? tags, System.Action onPressed)
    {
        PanelContainer card = UiTheme.CardButton(spine, out Button input, out VBoxContainer content);

        Label words = UiTheme.Prose(
            index < 9 ? Loc.TF("questui.dialogue.choice_number", index + 1, text) : text, UiTheme.Text);
        words.MouseFilter = Control.MouseFilterEnum.Ignore;
        content.AddChild(words);

        if (tags is { Count: > 0 })
        {
            var chips = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            chips.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
            chips.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
            foreach (ConsequenceTag tag in tags)
            {
                (string label, Color color) = TagLook(tag);
                chips.AddChild(UiTheme.Chip(label, color));
            }

            content.AddChild(chips);
        }

        input.Pressed += () => onPressed();
        _choiceActions.Add(onPressed);
        _list.AddChild(card);
    }
}
