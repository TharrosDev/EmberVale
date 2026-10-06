using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The options menu (Phase 24F): a modal panel with Graphics / Audio / Controls / Gameplay /
/// Accessibility sections, each control reading and writing the live <see cref="SettingsService"/>.
/// Reachable from both shells — the title <see cref="MainMenu"/> and the in-game
/// <see cref="PauseMenu"/> — which hide themselves behind it and restore on Back.
///
/// Changes apply <b>live</b> (every control calls <see cref="SettingsService.Apply"/> on change);
/// they persist to disk on Back and on each discrete toggle/dropdown change, and on a slider's
/// drag-end (so dragging a volume doesn't thrash the file). Built through <see cref="UiTheme"/>;
/// runs with <see cref="Node.ProcessModeEnum.Always"/> so it works while the game is paused.
/// </summary>
public partial class SettingsPanel : CanvasLayer
{
    private SettingsService _settings = null!;
    private System.Action? _onBack;

    /// <summary>Opens the panel as a child of <paramref name="parent"/>, invoking
    /// <paramref name="onBack"/> when the player backs out. No-op if no settings service exists.</summary>
    public static void Open(Node parent, System.Action? onBack = null)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            Log.Warn("Settings requested but no SettingsService is registered.");
            onBack?.Invoke();
            return;
        }

        var panel = new SettingsPanel { _settings = settings, _onBack = onBack };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 13; // above the main menu (11), pause menu (10), and slot panel (12)
        UiState.Open(this);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();
    }

    public override void _ExitTree()
    {
        UiState.Close(this);
    }

    public override void _Process(double delta)
    {
        // Esc or gamepad B backs out (matches the pause menu's feel); the PauseMenu suppresses its
        // own Esc while UiState.MenuOpen is set, so this can't also resume the game on the same press.
        if (Godot.Input.IsActionJustPressed(UiLive.Pause) ||
            Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            Back();
        }
    }

    private void Build()
    {
        var backdrop = UiTheme.Scrim(0.92f);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        UiTheme.ApplyWorkspace(panel, 0.68f);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceLg);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(col);

        col.AddChild(UiTheme.Title(Loc.T("settings.title")));
        col.AddChild(UiTheme.Divider());

        // The sections are tall, so scroll them. The scroll takes whatever height the workspace frame
        // leaves (37.5H made it viewport-relative; a fixed 420 overflowed a 533 px logical viewport).
        // Its floor is deliberately small: a floor near the frame's height pushed the panel to within
        // a few pixels of the window's bottom edge, because the frame grows to fit its content.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, ScrollMinHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, // keep the focused row in view under gamepad/keyboard nav (30.5J)
        };
        col.AddChild(scroll);

        // ⚠️ Reserve the gutter the vertical scrollbar draws in. A ScrollContainer paints its bar
        // *inside* its own rect, over the content — so a row sized to the full width had its
        // right-hand control sitting under the bar. The fix is a wider panel plus this inset, not a
        // taller one: the list is long by nature and shortening it only moves the problem.
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", UiTheme.SpaceLg);
        scroll.AddChild(gutter);

        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", UiTheme.RowGap);
        gutter.AddChild(body);

        BuildGraphics(body);
        BuildAudio(body);
        BuildControls(body);
        BuildGameplay(body);
        BuildAccessibility(body);

        col.AddChild(UiTheme.Divider());
        Button back = UiTheme.Action(Loc.T("common.back"));
        back.Pressed += Back;
        col.AddChild(back);

        UiFocus.GrabFirst(panel); // gamepad/keyboard land on the first setting (30.5J)
    }

    /// <summary>Width of the right-hand control column. Wide enough for the longest dropdown value
    /// and the slider-plus-readout pair, so nothing has to squeeze its label.</summary>
    private const float ControlColumn = 250f;

    /// <summary>Smallest height the settings list scrolls in; the workspace frame gives it the rest.</summary>
    private const float ScrollMinHeight = 220f;

    // --- Row builders -------------------------------------------------------

    /// <summary>An engraved section rule (37.5H). These were a plain accent-coloured body label,
    /// which put a section heading at exactly the same weight as the setting names underneath it -
    /// so a screen of thirty rows read as one undifferentiated column.</summary>
    private static void Section(VBoxContainer parent, string title)
    {
        parent.AddChild(UiTheme.SectionRule(title));
    }

    /// <summary>
    /// One setting: name on the left, control on the right, optional explanation underneath.
    ///
    /// The explanation is the point of the rebuild for the accessibility block. "Colour Vision" as
    /// a bare dropdown label asks the player to already know what deuteranopia is and what the game
    /// intends to do about it; a settings screen is exactly where that sentence belongs.
    /// </summary>
    private static Control Row(string label, Control control, string? explanation = null)
    {
        var wrap = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 0);

        // Every row is a full control tall, so a slider, a toggle and a dropdown share one pitch and the
        // rows stop touching: they were 18, 24 and 40 px high, which put the volume sliders 25 px apart.
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight) };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        Label name = UiTheme.Body(label);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(name);

        // Every control shares one right-hand column (37.5H). They were sized by their own content
        // before, so a long dropdown value pushed its label around while a checkbox left a gap —
        // thirty rows of that reads as a ragged edge rather than a column of values, and the widest
        // dropdown could squeeze its label until the two collided.
        var slot = new MarginContainer { CustomMinimumSize = new Vector2(ControlColumn, 0f) };
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (control is OptionButton)
        {
            control.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        }

        slot.AddChild(control);
        row.AddChild(slot);
        wrap.AddChild(row);

        if (!string.IsNullOrEmpty(explanation))
        {
            MarginContainer indent = UiTheme.Padding(0);
            indent.AddThemeConstantOverride("margin_left", UiTheme.SpaceMd);
            indent.AddThemeConstantOverride("margin_bottom", UiTheme.SpaceXs);

            Label note = UiTheme.Caption(explanation);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            indent.AddChild(note);
            wrap.AddChild(indent);
        }

        return wrap;
    }

    private Control ToggleRow(string label, bool value, System.Action<bool> onChanged, string? explanation = null)
    {
        CheckButton toggle = UiTheme.Toggle(value);
        toggle.Toggled += pressed => onChanged(pressed);
        return Row(label, toggle, explanation);
    }

    private Control DropdownRow(string label, string[] options, int selected, System.Action<int> onSelected, string? explanation = null)
    {
        OptionButton dropdown = UiTheme.Dropdown(options, selected);
        dropdown.ItemSelected += index => onSelected((int)index);
        return Row(label, dropdown, explanation);
    }

    /// <summary>A 0..1 volume slider with a live % readout; applies live while dragging, persists on
    /// release.</summary>
    private Control VolumeRow(string label, float value, System.Action<float> assign)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        HSlider slider = UiTheme.Slider(0d, 1d, 0.05d, value, 180f);
        Label readout = UiTheme.Body($"{Mathf.RoundToInt(value * 100f)}%", UiTheme.Dim);
        readout.CustomMinimumSize = new Vector2(48, 0);
        readout.HorizontalAlignment = HorizontalAlignment.Right;

        slider.ValueChanged += v =>
        {
            assign((float)v);
            readout.Text = $"{Mathf.RoundToInt((float)v * 100f)}%";
            _settings.Apply(); // live
        };
        slider.DragEnded += _ => Persist();
        box.AddChild(slider);
        box.AddChild(readout);
        return Row(label, box);
    }

    private Control SliderRow(string label, double min, double max, double step, float value, System.Action<double> assign, string? explanation = null)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        HSlider slider = UiTheme.Slider(min, max, step, value, 150f);
        Label readout = UiTheme.Body($"{value:0.00}", UiTheme.Dim);
        readout.CustomMinimumSize = new Vector2(48, 0);
        readout.HorizontalAlignment = HorizontalAlignment.Right;

        slider.ValueChanged += v =>
        {
            assign(v);
            readout.Text = $"{v:0.00}";
            _settings.Apply(); // live
        };
        slider.DragEnded += _ => Persist();
        box.AddChild(slider);
        box.AddChild(readout);
        return Row(label, box);
    }

    // --- Apply / persist ----------------------------------------------------

    /// <summary>Applies the live settings to the engine and writes them to disk. Used by discrete
    /// changes (toggles/dropdowns) and slider drag-ends; live slider drags only <c>Apply</c>.</summary>
    private void Persist()
    {
        _settings.Apply();
        _settings.Save();
    }

    private void Back()
    {
        _settings.Save(); // catch any live-applied-but-not-yet-persisted slider drag
        System.Action? onBack = _onBack;
        QueueFree();
        onBack?.Invoke();
    }
}
