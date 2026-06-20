using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
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
        _panel = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -300,
            OffsetRight = 300,
            OffsetTop = -260,
            OffsetBottom = -24,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
        };
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
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
        UiState.MenuOpen = open;

        bool playing = GameManager.Instance is { IsPlaying: true };
        Godot.Input.MouseMode = open || !playing
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

        var speaker = new Label { Text = _session.CurrentSpeaker() };
        speaker.AddThemeFontSizeOverride("font_size", 16);
        speaker.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.45f));
        _list.AddChild(speaker);

        var line = new Label
        {
            Text = node.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        line.AddThemeFontSizeOverride("font_size", 15);
        _list.AddChild(line);

        _list.AddChild(new HSeparator());

        List<DialogueChoice> choices = _session.VisibleChoices();
        if (choices.Count == 0)
        {
            // Dead-end node: offer a single way out so the player is never stuck.
            var leave = new Button { Text = "(Leave)", Alignment = HorizontalAlignment.Left };
            leave.Pressed += Close;
            _list.AddChild(leave);
            return;
        }

        foreach (DialogueChoice choice in choices)
        {
            DialogueChoice captured = choice;
            var button = new Button { Text = choice.Text, Alignment = HorizontalAlignment.Left };
            button.Pressed += () => Choose(captured);
            _list.AddChild(button);
        }
    }
}
