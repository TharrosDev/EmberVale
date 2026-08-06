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
        if (Godot.Input.IsActionJustPressed(GameInput.Pause) ||
            Godot.Input.IsActionJustPressed("ui_cancel"))
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
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(660, 0);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(18);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        pad.AddChild(col);

        col.AddChild(UiTheme.Title(Loc.T("settings.title")));
        col.AddChild(UiTheme.Divider());

        // The sections are tall, so scroll them. The height is viewport-relative (37.5H): a fixed
        // 420 plus the title and the Back button overflowed a 533 px logical viewport, which is
        // what a Steam Deck reports at UI scale 1.5.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(600, Mathf.Clamp(UiTheme.UsableHeight(panel) - 110f, 220f, 460f)),
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
        body.AddThemeConstantOverride("separation", 6);
        gutter.AddChild(body);

        var s = _settings.Current;

        Section(body, Loc.T("settings.section.graphics"));
        body.AddChild(DropdownRow(Loc.T("settings.window_mode"),
            new[] { Loc.T("settings.window_mode.windowed"), Loc.T("settings.window_mode.fullscreen"), Loc.T("settings.window_mode.borderless") },
            s.WindowMode, i => { s.WindowMode = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.vsync"), s.VSync, v => { s.VSync = v; Persist(); }));
        // Applies live: FOV is only judgeable by watching the world move under it.
        body.AddChild(SliderRow(Loc.T("settings.fov"), 60.0, 110.0, 1.0, s.FieldOfView,
            v => s.FieldOfView = (float)v));
        int[] fpsPresets = { 0, 30, 60, 120, 144 };
        body.AddChild(DropdownRow(Loc.T("settings.max_fps"),
            new[] { Loc.T("settings.max_fps.uncapped"), "30", "60", "120", "144" },
            System.Array.IndexOf(fpsPresets, s.MaxFps) is var fi && fi >= 0 ? fi : 0,
            i => { s.MaxFps = fpsPresets[i]; Persist(); }));

        Section(body, Loc.T("settings.section.audio"));
        body.AddChild(VolumeRow(Loc.T("settings.master_volume"), s.MasterVolume, v => s.MasterVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.music_volume"), s.MusicVolume, v => s.MusicVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.sfx_volume"), s.SfxVolume, v => s.SfxVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.ambience_volume"), s.AmbienceVolume, v => s.AmbienceVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.ui_volume"), s.UiVolume, v => s.UiVolume = v));
        body.AddChild(VolumeRow(Loc.T("settings.voice_volume"), s.VoiceVolume, v => s.VoiceVolume = v));

        Section(body, Loc.T("settings.section.controls"));
        body.AddChild(SliderRow(Loc.T("settings.mouse_sensitivity"), 0.05, 2.0, 0.05, s.MouseSensitivity,
            v => s.MouseSensitivity = (float)v));
        body.AddChild(ToggleRow(Loc.T("settings.invert_y"), s.InvertY, v => { s.InvertY = v; Persist(); }));

        Section(body, Loc.T("settings.section.gameplay"));
        body.AddChild(DropdownRow(Loc.T("settings.difficulty"),
            new[] { Loc.T("settings.difficulty.story"), Loc.T("settings.difficulty.normal"), Loc.T("settings.difficulty.hard") },
            s.Difficulty, i => { s.Difficulty = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.third_person"), s.ThirdPersonCamera,
            v => { s.ThirdPersonCamera = v; Persist(); }));

        // Both apply live, so dragging the distance or flipping the shoulder moves the camera while
        // the panel is open — which is the only way to actually judge either.
        body.AddChild(SliderRow(Loc.T("settings.tp_distance"), 2.0, 6.0, 0.1, s.ThirdPersonDistance,
            v => s.ThirdPersonDistance = (float)v));
        body.AddChild(DropdownRow(Loc.T("settings.tp_shoulder"),
            new[]
            {
                Loc.T("settings.tp_shoulder.right"),
                Loc.T("settings.tp_shoulder.left"),
                Loc.T("settings.tp_shoulder.centre"),
            },
            s.ThirdPersonShoulderSide, i => { s.ThirdPersonShoulderSide = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.show_tutorials"), s.ShowTutorials, v =>
        {
            s.ShowTutorials = v;
            Persist();

            // Applies live: switching tutorials off mid-game clears the hint on screen rather than
            // waiting for a restart to take effect.
            if (!v && ServiceLocator.Instance is { } locator &&
                locator.TryGet(out Onboarding.TutorialDirector tutorial))
            {
                tutorial.Skip();
            }
        }));

        Section(body, Loc.T("settings.section.accessibility"));
        body.AddChild(ToggleRow(Loc.T("settings.reduced_motion"), s.ReducedMotion,
            v => { s.ReducedMotion = v; Persist(); }, Loc.T("settings.reduced_motion.note")));
        body.AddChild(ToggleRow(Loc.T("settings.subtitles"), s.SubtitlesEnabled, v => { s.SubtitlesEnabled = v; Persist(); }));
        body.AddChild(SliderRow(Loc.T("settings.ui_scale"), 0.75, 1.5, 0.05, s.UiScale, v => s.UiScale = (float)v));

        // 37.5G. Text scale is separate from UI scale on purpose: UI scale is the window's content
        // scale factor and magnifies panels, margins and glyphs together, while this touches only
        // glyphs — for a player who wants readable text without surrendering half the screen to
        // chrome. It is floored at the 12 px legibility minimum inside UiTheme.FontSize.
        body.AddChild(SliderRow(Loc.T("settings.text_scale"), 0.85, 1.5, 0.05, s.TextScale,
            v => { s.TextScale = (float)v; Persist(); }, Loc.T("settings.text_scale.note")));

        body.AddChild(ToggleRow(Loc.T("settings.high_contrast"), s.HighContrast,
            v => { s.HighContrast = v; Persist(); }, Loc.T("settings.high_contrast.note")));

        // Colour-vision adaptation daltonizes the UI's semantic ramps — rarity, magic school,
        // faction standing, good/bad — so pairs that would collapse together stay apart. World art
        // is deliberately untouched; see ColorVision.
        body.AddChild(DropdownRow(
            Loc.T("settings.color_vision"),
            new[]
            {
                Loc.T("settings.color_vision.none"),
                Loc.T("settings.color_vision.deuteranopia"),
                Loc.T("settings.color_vision.protanopia"),
                Loc.T("settings.color_vision.tritanopia"),
            },
            (int)s.ColorVision,
            index => { s.ColorVision = (ColorVisionMode)index; Persist(); },
            Loc.T("settings.color_vision.note")));

        col.AddChild(UiTheme.Divider());
        Button back = UiTheme.Action(Loc.T("common.back"));
        back.CustomMinimumSize = new Vector2(0, 34);
        back.Pressed += Back;
        col.AddChild(back);

        UiFocus.GrabFirst(panel); // gamepad/keyboard land on the first setting (30.5J)
    }

    /// <summary>Width of the right-hand control column. Wide enough for the longest dropdown value
    /// and the slider-plus-readout pair, so nothing has to squeeze its label.</summary>
    private const float ControlColumn = 230f;

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

        var row = new HBoxContainer();
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
        box.AddThemeConstantOverride("separation", 8);
        HSlider slider = UiTheme.Slider(0d, 1d, 0.05d, value, 180f);
        Label readout = UiTheme.Body($"{Mathf.RoundToInt(value * 100f)}%", UiTheme.Dim);
        readout.CustomMinimumSize = new Vector2(40, 0);
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
        box.AddThemeConstantOverride("separation", 8);
        HSlider slider = UiTheme.Slider(min, max, step, value, 150f);
        Label readout = UiTheme.Body($"{value:0.00}", UiTheme.Dim);
        readout.CustomMinimumSize = new Vector2(40, 0);
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
