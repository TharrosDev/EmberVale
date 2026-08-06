using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Onboarding;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The onboarding's entire visible footprint (Phase 33B): one framed line above the hotbar naming
/// the verb being taught and the key that performs it. It appears when a hint starts, clears when
/// the player performs it, and is absent the rest of the game — no tutorial pop-ups, no modal
/// windows, nothing to dismiss.
///
/// The key glyph is resolved live through <see cref="GameInput.PromptLabel"/>, so a rebind (or
/// picking up a gamepad) never leaves the hint naming a key that does nothing.
/// </summary>
public partial class TutorialHint : VBoxContainer
{
    private PanelContainer _frame = null!;
    private Label _label = null!;
    private TutorialStep _step = TutorialStep.None;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // A Card, not a Panel (37.5H). One line of teaching text had been wearing a full brass
        // frame plus a grain shader, which made the least important thing on screen the most
        // ornamented.
        _frame = UiTheme.Card(UiTheme.Accent);
        _frame.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_frame);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        _frame.AddChild(pad);

        _label = UiTheme.Body(string.Empty, UiTheme.Text);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        pad.AddChild(_label);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<TutorialStepChangedEvent>(OnStepChanged);
        bus?.Subscribe<TutorialStepCompletedEvent>(OnStepCompleted);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        if (bus == null)
        {
            return;
        }

        bus.Unsubscribe<TutorialStepChangedEvent>(OnStepChanged);
        bus.Unsubscribe<TutorialStepCompletedEvent>(OnStepCompleted);
    }

    public override void _Process(double delta)
    {
        // Re-resolve the glyph each frame the hint is up: the player may switch to a gamepad
        // mid-hint, and a hint naming the wrong input is worse than none.
        if (Visible && _step != TutorialStep.None)
        {
            _label.Text = HintText(_step);
        }
    }

    private void OnStepChanged(TutorialStepChangedEvent e)
    {
        _step = e.Step;
        Visible = e.Step != TutorialStep.None;
        if (Visible)
        {
            _label.Text = HintText(e.Step);
        }
    }

    // The completed hint clears immediately; the director holds the gap before the next one.
    private void OnStepCompleted(TutorialStepCompletedEvent e)
    {
        if (_step == e.Step)
        {
            _step = TutorialStep.None;
            Visible = false;
        }
    }

    private static string HintText(TutorialStep step)
    {
        string key = TutorialScript.HintKey(step);
        if (key.Length == 0)
        {
            return string.Empty;
        }

        string action = TutorialScript.ActionFor(step);

        // Looking has no bound action — its copy names the mouse itself, so it takes no glyph.
        return action.Length == 0 ? Loc.T(key) : Loc.TF(key, GameInput.PromptLabel(action));
    }
}
