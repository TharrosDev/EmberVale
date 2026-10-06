using System.Collections.Generic;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>The small prompts <see cref="SettingsPanel"/> lays over its sheet: a confirm, the
/// binding conflict, and the "press a key" state. One at a time; each holds focus until it closes.</summary>
public partial class SettingsPanel
{
    private Control? _prompt;
    private Control? _focusBeforePrompt;

    /// <summary>One button of a prompt. <paramref name="Focused"/> marks the one focus starts on.</summary>
    private readonly record struct PromptChoice(string Label, UiCue Cue, System.Action Chosen, bool Focused = false);

    /// <summary>
    /// Opens a prompt over the sheet: a title, a line of text, an optional extra row and a row of
    /// choices. The scrim under it takes the pointer, and focus cannot walk out of the choices, so
    /// the sheet underneath is out of reach until one is taken (or Esc / B closes it).
    /// Returns the label holding the text, for a prompt that rewrites it while open.
    /// </summary>
    private Label OpenPrompt(string title, string text, Control? extra, params PromptChoice[] choices)
    {
        Control? before = _prompt == null ? GetViewport()?.GuiGetFocusOwner() : _focusBeforePrompt;
        ClosePrompt(restoreFocus: false);
        _focusBeforePrompt = before;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect scrim = UiTheme.Scrim(0.6f);
        scrim.MouseFilter = Control.MouseFilterEnum.Stop;
        root.AddChild(scrim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(centre);

        // A plate with one lit edge. Its width follows the view so it still fits a handheld.
        float width = Mathf.Min(UiTheme.SettingsPromptWidth,
            GetViewport().GetVisibleRect().Size.X - (UiTheme.SpaceXl * 2f));
        PanelContainer plate = UiTheme.Card(UiTheme.RuleLit);
        centre.AddChild(plate);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        plate.AddChild(col);

        var heading = new Label { Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        UiTheme.ApplyType(heading, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        heading.AddThemeColorOverride("font_color", UiTheme.Accent);
        col.AddChild(heading);

        Label body = UiTheme.Body(text);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(body);

        if (extra != null)
        {
            col.AddChild(extra);
        }

        var buttons = new List<Button>();
        Button? first = null;
        if (choices.Length > 0)
        {
            HFlowContainer row = UiTheme.FlowRow();
            col.AddChild(row);
            foreach (PromptChoice choice in choices)
            {
                Button button = UiTheme.Action(choice.Label, choice.Cue);
                System.Action chosen = choice.Chosen;
                button.Pressed += () =>
                {
                    ClosePrompt();
                    chosen();
                };
                row.AddChild(button);
                buttons.Add(button);
                if (choice.Focused)
                {
                    first = button;
                }
            }
        }

        _prompt = root;
        AddChild(root);
        if (_legend is { } legend)
        {
            MoveChild(legend, -1); // it names this prompt's keys, so it stays above the scrim
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
        else
        {
            GetViewport()?.GuiReleaseFocus();
        }

        UiFx.FadeIn(plate, UiTheme.DurationFast);
        UpdateLegend();
        return body;
    }

    /// <summary>Closes the open prompt, at once, and hands focus back to where it was.</summary>
    private void ClosePrompt(bool restoreFocus = true)
    {
        if (_prompt == null)
        {
            return;
        }

        // Hidden now and freed at the end of the frame: this is usually called from inside one of
        // the prompt's own button signals.
        _listening = null;
        _prompt.Visible = false;
        _prompt.QueueFree();
        _prompt = null;

        // The press that closed it is still "just pressed" this frame; it must not also back out
        // of the menu or step its tabs.
        _quietUntilFrame = Engine.GetProcessFrames() + 2;
        if (!restoreFocus)
        {
            return;
        }

        if (_focusBeforePrompt is { } before && IsInstanceValid(before) && before.IsInsideTree() && before.IsVisibleInTree())
        {
            before.GrabFocus();
        }
        else
        {
            UiFocus.GrabFirst(_scroll);
        }

        _focusBeforePrompt = null;
        UpdateLegend();
    }

    /// <summary>Asks before doing <paramref name="confirmed"/>, with Cancel focused.</summary>
    private void OpenConfirm(string title, string text, System.Action confirmed)
    {
        OpenPrompt(title, text, null,
            new PromptChoice(Loc.T("common.cancel"), UiCue.Back, () => { }, Focused: true),
            new PromptChoice(title, UiCue.Confirm, confirmed));
    }
}
