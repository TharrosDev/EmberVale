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
        UiState.MenuOpen = true;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();
    }

    public override void _ExitTree()
    {
        UiState.MenuOpen = false;
    }

    public override void _Process(double delta)
    {
        // Esc backs out (matches the pause menu's feel); the PauseMenu suppresses its own Esc while
        // UiState.MenuOpen is set, so this can't also resume the game on the same press.
        if (Godot.Input.IsActionJustPressed(GameInput.Pause))
        {
            Back();
        }
    }

    private void Build()
    {
        var backdrop = new ColorRect { Color = new Color(0.02f, 0.02f, 0.04f, 0.92f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        PanelContainer panel = UiTheme.Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(480, 0);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(18);
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        pad.AddChild(col);

        Label header = UiTheme.Header(Loc.T("settings.title"));
        col.AddChild(header);
        col.AddChild(new HSeparator());

        // The sections are tall, so scroll them; cap the height so the panel never exceeds the screen.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(440, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        col.AddChild(scroll);

        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(body);

        var s = _settings.Current;

        Section(body, Loc.T("settings.section.graphics"));
        body.AddChild(DropdownRow(Loc.T("settings.window_mode"),
            new[] { Loc.T("settings.window_mode.windowed"), Loc.T("settings.window_mode.fullscreen"), Loc.T("settings.window_mode.borderless") },
            s.WindowMode, i => { s.WindowMode = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.vsync"), s.VSync, v => { s.VSync = v; Persist(); }));
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

        Section(body, Loc.T("settings.section.accessibility"));
        body.AddChild(ToggleRow(Loc.T("settings.reduced_motion"), s.ReducedMotion, v => { s.ReducedMotion = v; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.subtitles"), s.SubtitlesEnabled, v => { s.SubtitlesEnabled = v; Persist(); }));
        body.AddChild(SliderRow(Loc.T("settings.ui_scale"), 0.75, 1.5, 0.05, s.UiScale, v => s.UiScale = (float)v));

        col.AddChild(new HSeparator());
        Button back = UiTheme.Action(Loc.T("common.back"));
        back.CustomMinimumSize = new Vector2(0, 34);
        back.Pressed += Back;
        col.AddChild(back);
    }

    // --- Row builders -------------------------------------------------------

    private static void Section(VBoxContainer parent, string title)
    {
        var label = UiTheme.Body(title, UiTheme.Accent);
        label.AddThemeFontSizeOverride("font_size", UiTheme.BodyFontSize);
        parent.AddChild(label);
    }

    private static HBoxContainer Row(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Label name = UiTheme.Body(label);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(control);
        return row;
    }

    private HBoxContainer ToggleRow(string label, bool value, System.Action<bool> onChanged)
    {
        CheckButton toggle = UiTheme.Toggle(value);
        toggle.Toggled += pressed => onChanged(pressed);
        return Row(label, toggle);
    }

    private HBoxContainer DropdownRow(string label, string[] options, int selected, System.Action<int> onSelected)
    {
        OptionButton dropdown = UiTheme.Dropdown(options, selected);
        dropdown.ItemSelected += index => onSelected((int)index);
        return Row(label, dropdown);
    }

    /// <summary>A 0..1 volume slider with a live % readout; applies live while dragging, persists on
    /// release.</summary>
    private HBoxContainer VolumeRow(string label, float value, System.Action<float> assign)
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

    private HBoxContainer SliderRow(string label, double min, double max, double step, float value, System.Action<double> assign)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        HSlider slider = UiTheme.Slider(min, max, step, value, 180f);
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
