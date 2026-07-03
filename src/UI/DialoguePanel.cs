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

        _list.AddChild(UiTheme.Header(Loc.T(_session.CurrentSpeaker())));

        Label line = UiTheme.Body(Loc.T(node.Text));
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.AddThemeFontSizeOverride("font_size", 15);
        _list.AddChild(line);

        _list.AddChild(new HSeparator());

        List<DialogueChoice> choices = _session.VisibleChoices();
        if (choices.Count == 0)
        {
            // Dead-end node: offer a single way out so the player is never stuck.
            Button leave = UiTheme.Action(Loc.T("dialogue.leave"));
            leave.Alignment = HorizontalAlignment.Left;
            leave.Pressed += Close;
            _list.AddChild(leave);
            return;
        }

        foreach (DialogueChoice choice in choices)
        {
            DialogueChoice captured = choice;
            Button button = UiTheme.Action(Loc.T(choice.Text));
            button.Alignment = HorizontalAlignment.Left;
            button.Pressed += () => Choose(captured);
            _list.AddChild(button);
        }
    }
}
