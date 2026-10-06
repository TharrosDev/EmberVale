using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Onboarding;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The onboarding's entire visible footprint (Phase 33B): one line above the hotbar naming the verb
/// being taught and the input that performs it. It appears when a hint starts, clears when the
/// player performs it, and is absent the rest of the game — no tutorial pop-ups, no modal windows,
/// nothing to dismiss.
///
/// It is the interaction prompt's sibling and is built like it: the same HUD plate with its one lit
/// edge, the input's glyph first (<see cref="UiGlyph"/>), then the sentence. Both follow the device
/// and the bindings by event, so a rebind (or picking up a gamepad) never leaves the hint naming a
/// key that does nothing, and the widget has no frame callback at all.
/// </summary>
public partial class TutorialHint : VBoxContainer
{
    private PanelContainer _frame = null!;
    private Control _glyph = null!;
    private Label _label = null!;
    private TutorialStep _step = TutorialStep.None;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        _frame = UiTheme.HudPlate(UiTheme.Accent);
        _frame.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddChild(_frame);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _frame.AddChild(row);

        _glyph = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(_glyph);

        _label = UiTheme.HudInk(UiTheme.Body(string.Empty, UiTheme.Text));
        _label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_label);

        EventBus bus = EventBus.Instance;
        bus?.Subscribe<TutorialStepChangedEvent>(OnStepChanged);
        bus?.Subscribe<TutorialStepCompletedEvent>(OnStepCompleted);
        bus?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
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
        bus.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        bus.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    /// <summary>A locale switch re-resolves the sentence.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged && _label != null)
        {
            Refresh();
        }
    }

    private void OnStepChanged(TutorialStepChangedEvent e)
    {
        _step = e.Step;
        Visible = e.Step != TutorialStep.None;
        Refresh();
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

    // The player may switch to a gamepad, or rebind the key, while the hint is up.
    private void OnDeviceChanged(InputDeviceChangedEvent e) => Refresh();

    private void OnBindingsChanged(InputBindingsChangedEvent e) => Refresh();

    /// <summary>Restates the glyph and the sentence for the step being taught.</summary>
    private void Refresh()
    {
        if (_step == TutorialStep.None)
        {
            return;
        }

        foreach (Node child in _glyph.GetChildren())
        {
            _glyph.RemoveChild(child);
            child.QueueFree();
        }

        // Looking has no bound action - its copy names the mouse itself, so it takes no glyph.
        string action = TutorialScript.ActionFor(_step);
        _glyph.Visible = action.Length > 0;
        if (action.Length > 0)
        {
            _glyph.AddChild(UiGlyph.For(action));
        }

        _label.Text = HintText(_step);
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
