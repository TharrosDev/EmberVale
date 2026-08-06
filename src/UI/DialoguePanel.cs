using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Localization;
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
/// </summary>
public partial class DialoguePanel : UiPanel
{
    // A conversation ends through its choices (or "Leave"), never a cancel press — closing on
    // B/Esc would strand the session and skip the DialogueEndedEvent (30.5J).
    protected override bool CloseOnCancel => false;

    private VBoxContainer _list = null!;

    private DialogueSession? _session;
    private IEntity? _player;
    private DialogueResource? _dialogue;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.AnchorTop = 1f;
        shell.AnchorBottom = 1f;
        shell.OffsetLeft = -300;
        shell.OffsetRight = 300;
        shell.OffsetTop = -260;
        shell.OffsetBottom = -24;
        shell.GrowHorizontal = Control.GrowDirection.Both;
        shell.GrowVertical = Control.GrowDirection.Begin;

        MarginContainer margin = UiTheme.Padding(14);
        shell.AddChild(margin);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(_list);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
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

        if (_session.Choose(choice))
        {
            Close();
        }
        else
        {
            MarkDirty();
        }
    }

    private void Close()
    {
        DialogueResource? dialogue = _dialogue;
        IEntity? player = _player;

        _session = null;
        _dialogue = null;
        _player = null;
        SetOpen(false);

        if (player != null && dialogue != null)
        {
            EventBus.Instance?.Publish(new DialogueEndedEvent(player, dialogue));
        }
    }

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        if (_session?.CurrentNode is not { } node)
        {
            return;
        }

        // The illuminated page (37.5E): the speaker is carved, the words are set in the book serif,
        // and the choices are cards rather than a stack of buttons. Dialogue is the only screen in
        // the game the player *reads* rather than scans, so it is the one that most rewards the
        // typography split -- and the one where a row of identical grey buttons most obviously
        // reads as a form.
        Label speaker = UiTheme.Title(Loc.T(_session.CurrentSpeaker()));
        _list.AddChild(speaker);
        _list.AddChild(UiTheme.Divider());

        _list.AddChild(UiTheme.Prose(Loc.T(node.Text)));

        _list.AddChild(UiTheme.Divider());

        List<DialogueChoice> choices = _session.VisibleChoices();
        if (choices.Count == 0)
        {
            // Dead-end node: offer a single way out so the player is never stuck.
            _list.AddChild(ChoiceCard(Loc.T("dialogue.leave"), UiTheme.Dim, Close));
            return;
        }

        foreach (DialogueChoice choice in choices)
        {
            DialogueChoice captured = choice;

            // A choice that starts a quest or ends the conversation is worth marking apart from
            // ordinary talk -- the spine is the cheapest way to say so without adding a legend.
            Color spine = choice.Effect == DialogueEffect.StartQuest ? UiTheme.QuestMain
                : string.IsNullOrEmpty(choice.Goto) ? UiTheme.Dim
                : UiTheme.Accent;

            _list.AddChild(ChoiceCard(Loc.T(choice.Text), spine, () => Choose(captured)));
        }
    }

    /// <summary>One dialogue choice as an engraved card. The whole card is the button, so the
    /// target is the full row rather than the text's own width -- which also means the focus rule a
    /// gamepad follows matches what a mouse can click.</summary>
    private Control ChoiceCard(string text, Color spine, System.Action onPressed)
    {
        var button = new Button { Flat = true, Alignment = HorizontalAlignment.Left };
        UiTheme.ApplyType(button, UiTheme.FontRole.Serif, UiTheme.BodyFontSize);
        button.Text = text;
        button.AddThemeColorOverride("font_color", UiTheme.Text);
        button.AddThemeColorOverride("font_hover_color", UiTheme.Accent);
        button.AddThemeColorOverride("font_focus_color", UiTheme.Accent);

        StyleBoxFlat normal = UiTheme.CardStyle(spine);
        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = UiTheme.CardBg with { A = 1f };
        StyleBoxFlat focus = (StyleBoxFlat)hover.Duplicate();
        focus.BorderColor = UiTheme.Accent;
        focus.SetBorderWidthAll(1);
        focus.BorderWidthLeft = 3;

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", focus);
        button.Pressed += () => onPressed();
        return button;
    }
}
