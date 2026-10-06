using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The small question the session shell lays over a screen: delete this save, load over unsaved
/// progress, resume or skip a narration. A plate with one lit edge on a scrim, a title, a line of
/// text and a row of choices; the same shape as the settings screen's prompts.
///
/// The scrim takes the pointer and focus cannot walk out of the choices, so the screen underneath
/// is out of reach until one is taken. The prompt does not listen for cancel itself: the screen
/// that opened it already polls <c>ui_cancel</c>, and two listeners on one press is how a single
/// Esc closes two things. That screen calls <see cref="Close"/>.
/// </summary>
public sealed partial class SessionPrompt : Control
{
    /// <summary>One button of a prompt. <paramref name="Focused"/> marks the one focus starts on
    /// (the answer that loses nothing); <paramref name="Danger"/> the one that destroys something.</summary>
    public readonly record struct Choice(string Label, UiCue Cue, Action Chosen, bool Focused = false, bool Danger = false);

    private readonly string _title;
    private readonly string _text;
    private readonly Choice[] _choices;
    private Control? _focusBefore;

    /// <summary>False once a choice was taken or <see cref="Close"/> was called.</summary>
    public bool IsOpen { get; private set; } = true;

    private SessionPrompt(string title, string text, Choice[] choices)
    {
        _title = title;
        _text = text;
        _choices = choices;
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always; // asked over a paused world as often as not
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Opens a prompt as the last child of <paramref name="parent"/> and gives it focus.</summary>
    public static SessionPrompt Open(Node parent, string title, string text, params Choice[] choices)
    {
        var prompt = new SessionPrompt(title, text, choices);
        prompt._focusBefore = parent.GetViewport()?.GuiGetFocusOwner();
        parent.AddChild(prompt);
        return prompt;
    }

    public override void _Ready()
    {
        ColorRect scrim = UiTheme.Scrim(0.6f);
        scrim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(scrim);

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        // Its width follows the view so it still fits a handheld.
        float width = Mathf.Min(UiTheme.SessionPromptWidth, GetViewportRect().Size.X - (UiTheme.SpaceXl * 2f));
        PanelContainer plate = UiTheme.Card(UiTheme.RuleLit);
        centre.AddChild(plate);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        plate.AddChild(col);

        // The interface face, not the carved one: a question can run past three words.
        var heading = new Label { Text = _title, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        UiTheme.ApplyType(heading, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        col.AddChild(heading);

        if (_text.Length > 0)
        {
            Label body = UiTheme.Body(_text);
            body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(body);
        }

        HFlowContainer row = UiTheme.FlowRow();
        col.AddChild(row);

        var buttons = new List<Button>();
        Button? first = null;
        foreach (Choice choice in _choices)
        {
            Button button = UiTheme.Action(choice.Label, choice.Cue);
            if (choice.Danger)
            {
                button.AddThemeColorOverride("font_color", UiTheme.Bad);
            }

            Action chosen = choice.Chosen;
            button.Pressed += () =>
            {
                Close();
                chosen();
            };
            row.AddChild(button);
            buttons.Add(button);
            if (choice.Focused)
            {
                first = button;
            }
        }

        // Focus stays inside the prompt: each choice's neighbours are the other choices, wrapping,
        // and up and down go nowhere. Paths, so only now that the buttons are in the tree.
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            NodePath previous = button.GetPathTo(buttons[(i + buttons.Count - 1) % buttons.Count]);
            NodePath next = button.GetPathTo(buttons[(i + 1) % buttons.Count]);
            NodePath self = button.GetPathTo(button);
            button.FocusNeighborLeft = previous;
            button.FocusPrevious = previous;
            button.FocusNeighborRight = next;
            button.FocusNext = next;
            button.FocusNeighborTop = self;
            button.FocusNeighborBottom = self;
        }

        if (buttons.Count > 0)
        {
            (first ?? buttons[0]).GrabFocus();
        }

        UiFx.FadeIn(plate, UiTheme.DurationFast);
    }

    /// <summary>Closes the prompt, at once, and hands focus back to where it was.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        // Hidden now and freed at the end of the frame: this is usually called from inside one of
        // the prompt's own button signals.
        IsOpen = false;
        Visible = false;
        QueueFree();

        if (_focusBefore is { } before && IsInstanceValid(before) && before.IsInsideTree() && before.IsVisibleInTree())
        {
            before.GrabFocus();
        }
    }
}
