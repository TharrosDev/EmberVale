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
/// While open it is modal — like the character screen it frees the mouse and sets
/// <see cref="UiState.MenuOpen"/> so the player controller stops driving the character.
/// Rebuilds happen from a dirty flag in <c>_Process</c> (never during a button signal)
/// so a choice never frees its own button mid-callback.
/// </summary>
public partial class DialoguePanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;

    private DialogueSession? _session;
    private IEntity? _player;
    private DialogueResource? _dialogue;
    private bool _dirty;

    public override void _Ready()
    {
        _panel = UiTheme.Panel();
        _panel.Visible = false;
        _panel.AnchorLeft = 0.5f;
        _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -300;
        _panel.OffsetRight = 300;
        _panel.OffsetTop = -260;
        _panel.OffsetBottom = -24;
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        AddChild(_panel);

        MarginContainer margin = UiTheme.Padding(14);
        _panel.AddChild(margin);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 8);
        margin.AddChild(_list);

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
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (_panel.Visible && _dirty)
        {
            Rebuild();
        }
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
            _dirty = true;
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

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        if (open) UiState.Open(this); else UiState.Close(this);

        bool playing = GameManager.Instance is { IsPlaying: true };
        Godot.Input.MouseMode = UiState.MenuOpen || !playing
            ? Godot.Input.MouseModeEnum.Visible
            : Godot.Input.MouseModeEnum.Captured;
    }

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

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
