using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Save;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The five questions a first launch asks before the title: subtitles, text size, high contrast,
/// reduced motion and colour vision, each beside a sample of what it changes. They are the
/// settings a player may need before they can read the menu that holds the settings.
///
/// Shown once, by <see cref="MainMenu"/>, on a true first run only (<see cref="ConsumeWanted"/>).
/// Every answer is applied and saved as it is given, and all five stay in Settings afterwards.
/// The sheet is rebuilt in place from a dirty flag when an answer changes how it is drawn.
/// </summary>
public partial class FirstRunSetup : CanvasLayer
{
    private static bool _asked;

    private SettingsService _settings = null!;
    private System.Action? _onDone;
    private Control? _root;
    private UiLegend? _legend;
    private ScrollContainer _scroll = null!;
    private ScrollContainer _samples = null!;
    private Control _subtitleSample = null!;
    private Control _textWell = null!;
    private Control _visionSample = null!;
    private Control _motionSample = null!;
    private Control? _sampleWanted;
    private Viewport? _viewport;
    private bool _dirty;

    private Label? _textSample;
    private Label? _captionSample;
    private System.Action? _subtitleLive;

    /// <summary>
    /// Whether first-run setup should open, asked once per process: the answer is true only for a
    /// person at the machine (<see cref="ShellFrontRules.Attended"/>) with no saves and a settings
    /// file this very boot wrote. Later calls return false, so quitting to the title, or deleting
    /// the last save in the same sitting, never brings it back.
    /// </summary>
    public static bool ConsumeWanted()
    {
        if (_asked)
        {
            return false;
        }

        _asked = true;
        bool attended = ShellFrontRules.Attended(
            DisplayServer.GetName() == "headless",
            !string.IsNullOrWhiteSpace(OS.GetEnvironment("EMBERVALE_USER_DIR")),
            OS.GetCmdlineUserArgs().Length);
        if (!attended)
        {
            return false; // a tool run never touches the user folder to find out
        }

        string path = UserDataPaths.Resolve("settings.tres");
        bool exists = FileAccess.FileExists(path);
        double started = Time.GetUnixTimeFromSystem() - (Time.GetTicksMsec() / 1000d);
        return ShellFrontRules.FirstRun(
            attended,
            SaveManager.Instance?.ListSlots().Count ?? 0,
            exists,
            exists ? FileAccess.GetModifiedTime(path) : 0d,
            started);
    }

    /// <summary>Opens the screen over <paramref name="parent"/>; <paramref name="onDone"/> runs
    /// when the player continues. With no settings service there is nothing to ask: it runs at
    /// once and null is returned.</summary>
    public static FirstRunSetup? Open(Node parent, System.Action? onDone)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            onDone?.Invoke();
            return null;
        }

        var screen = new FirstRunSetup { _settings = settings, _onDone = onDone };
        parent.AddChild(screen);
        return screen;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 13; // over the title (11), like the settings panel; under the boot splash (14)
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;

        Build();
        UiFocus.GrabFirst(_scroll);
        UiFx.FadeIn(_root!, UiTheme.DurationSlow);

        _viewport = GetViewport();
        _viewport.SizeChanged += MarkDirty;
    }

    public override void _ExitTree()
    {
        if (_viewport != null)
        {
            _viewport.SizeChanged -= MarkDirty;
        }
    }

    private void MarkDirty() => _dirty = true;

    public override void _Process(double delta)
    {
        // The sample for the question in focus, brought into view: a pad has no wheel to do it.
        // A tick after the focus moved, so a sheet rebuilt this frame has been laid out.
        if (_sampleWanted != null && !_dirty)
        {
            if (IsInstanceValid(_sampleWanted) && _samples.IsAncestorOf(_sampleWanted))
            {
                _samples.EnsureControlVisible(_sampleWanted);
            }

            _sampleWanted = null;
        }

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        int[]? focus = _root != null ? UiFocus.PathOf(_root) : null;
        if (_root != null)
        {
            RemoveChild(_root);
            _root.QueueFree();
        }

        Build();
        if (focus != null)
        {
            UiFocus.Restore(_root!, focus);
        }
        else
        {
            UiFocus.GrabFirst(_scroll);
        }
    }

    // --- The sheet ----------------------------------------------------------

    private void Build()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Min(view.X - (UiChromeRules.Gutter(view.X) * 2f), UiTheme.FirstRunSheetWidth);
        bool wide = width >= UiTheme.FirstRunSampleWidth * 2.2f;

        (Control root, VBoxContainer col) = UiTheme.Sheet(width, 1f, centred: true);
        col.SizeFlagsVertical = Control.SizeFlags.ExpandFill; // so the list can take the height and scroll

        // The first screen a new player sees: the title's opening painting, dimmed, behind it.
        UiTheme.SheetOverPainting(root, UiTheme.Painting(ShellFrontRules.TitlePainting(1)));
        _root = root;
        AddChild(root);
        MoveChild(root, 0);

        // Made again with the sheet: its captions and plate follow text size and contrast.
        if (_legend != null)
        {
            RemoveChild(_legend);
            _legend.QueueFree();
        }

        _legend = new UiLegend();
        AddChild(_legend);
        _legend.Set(new[] { new LegendEntry("ui_accept", Loc.T("title.legend.select")) });

        col.AddChild(UiTheme.Title(Loc.T("firstrun.title")));
        Label intro = UiTheme.Body(Loc.T("firstrun.intro"), UiTheme.Dim);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(intro);
        col.AddChild(UiOrnament.EmberWipe());

        BoxContainer split = wide ? new HBoxContainer() : new VBoxContainer();
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        split.AddThemeConstantOverride("separation", wide ? UiTheme.SpaceLg : UiTheme.SpaceSm);
        col.AddChild(split);

        // Beside the questions where there is room, under them where there is not. It scrolls
        // rather than push the Continue button off a short screen at a large text size. Built
        // first: each question brings its own sample into view.
        Control samples = BuildSamples();

        (ScrollContainer scroll, VBoxContainer rows) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight * 2f);
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll = scroll;
        split.AddChild(scroll);
        BuildRows(rows);

        if (wide)
        {
            samples.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            samples.CustomMinimumSize = new Vector2(UiTheme.FirstRunSampleWidth + UiTheme.ScrollGutter, 0f);
        }
        else
        {
            samples.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight * 2f);
        }

        split.AddChild(samples);

        Button done = UiTheme.Action(Loc.T("firstrun.continue"), UiCue.Confirm);
        done.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        done.Pressed += Done;
        col.AddChild(done);
    }

    private void BuildRows(VBoxContainer rows)
    {
        var s = _settings.Current;

        rows.AddChild(Row(Loc.T("settings.subtitles"), Toggle(s.SubtitlesEnabled, v =>
        {
            s.SubtitlesEnabled = v;
            Persist();
            _subtitleLive?.Invoke();
        }), _subtitleSample));

        rows.AddChild(Row(Loc.T("settings.text_scale"), TextScaleSlider(), _textWell));

        // These two change how every surface here is drawn, so the sheet is rebuilt.
        rows.AddChild(Row(Loc.T("settings.high_contrast"), Toggle(s.HighContrast, v =>
        {
            s.HighContrast = v;
            Persist();
            MarkDirty();
        }), _visionSample));

        rows.AddChild(Row(Loc.T("settings.reduced_motion"), Toggle(s.ReducedMotion, v =>
        {
            s.ReducedMotion = v;
            Persist();
            MarkDirty();
        }), _motionSample));

        OptionButton vision = UiTheme.Dropdown(
            new[]
            {
                Loc.T("settings.color_vision.none"), Loc.T("settings.color_vision.deuteranopia"),
                Loc.T("settings.color_vision.protanopia"), Loc.T("settings.color_vision.tritanopia"),
            },
            (int)s.ColorVision);
        vision.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        vision.ItemSelected += index =>
        {
            s.ColorVision = (ColorVisionMode)(int)index;
            Persist();
            MarkDirty();
        };
        rows.AddChild(Row(Loc.T("settings.color_vision"), vision, _visionSample));
    }

    private static CheckButton Toggle(bool value, System.Action<bool> onChanged)
    {
        CheckButton toggle = UiTheme.Toggle(value);
        toggle.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight); // the whole row height presses it
        toggle.Toggled += pressed => onChanged(pressed);
        return toggle;
    }

    /// <summary>
    /// The text-size slider. The sample follows the handle; the interface itself takes the size
    /// when the handle is let go (or at once for a step from the keyboard or a pad), because
    /// resizing the sheet under a drag moves the slider out from under the pointer.
    /// </summary>
    private Control TextScaleSlider()
    {
        var s = _settings.Current;
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        HSlider slider = UiTheme.Slider(0.85, 1.5, 0.05, s.TextScale,
            UiTheme.FirstRunControlColumn - UiTheme.SettingsReadout - UiTheme.SpaceSm);
        slider.CustomMinimumSize = new Vector2(slider.CustomMinimumSize.X, UiTheme.ControlHeight);
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.Scrollable = false; // the wheel scrolls the list
        Label readout = UiTheme.Body(Percent(s.TextScale), UiTheme.Dim);
        readout.CustomMinimumSize = new Vector2(UiTheme.SettingsReadout, 0f);
        readout.HorizontalAlignment = HorizontalAlignment.Right;

        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.ValueChanged += v =>
        {
            s.TextScale = (float)v;
            readout.Text = Percent((float)v);
            SizeTextSample();
            if (!dragging)
            {
                Persist();
                MarkDirty();
            }
        };
        slider.DragEnded += changed =>
        {
            dragging = false;
            if (changed)
            {
                Persist();
                MarkDirty();
            }
        };

        box.AddChild(slider);
        box.AddChild(readout);
        return box;
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

    /// <summary>One question: its name, and its control in a shared right-hand column. The row
    /// lifts to a card with the lit edge while its control holds focus, as a settings row does,
    /// and <paramref name="sample"/> is scrolled into view beside it.</summary>
    private Control Row(string title, Control control, Control sample)
    {
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        frame.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(false));

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight) };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        frame.AddChild(row);

        Label name = UiTheme.Body(title);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(name);

        var slot = new MarginContainer { CustomMinimumSize = new Vector2(UiTheme.FirstRunControlColumn, 0f) };
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slot.AddChild(control);
        row.AddChild(slot);

        foreach (Control focusable in Focusables(control))
        {
            focusable.FocusEntered += () =>
            {
                frame.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(true));
                _sampleWanted = sample;
            };
            focusable.FocusExited += () => frame.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(false));
        }

        return frame;
    }

    private static IEnumerable<Control> Focusables(Control root)
    {
        if (root.FocusMode != Control.FocusModeEnum.None)
        {
            yield return root;
        }

        foreach (Node child in root.GetChildren())
        {
            if (child is Control control)
            {
                foreach (Control found in Focusables(control))
                {
                    yield return found;
                }
            }
        }
    }

    // --- Samples ------------------------------------------------------------

    /// <summary>What the five answers look like, in the order they are asked: a subtitle on its
    /// plate, text at the chosen size, the colours that carry meaning, and whether things move.</summary>
    private Control BuildSamples()
    {
        (ScrollContainer scroll, VBoxContainer col) = UiTheme.ScrollList();
        scroll.FollowFocus = false; // nothing in it takes focus; the rows bring their samples up
        _samples = scroll;
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        col.AddChild(UiTheme.Caption(Loc.T("firstrun.sample")));
        col.AddChild(_subtitleSample = SubtitleSample());
        col.AddChild(_textWell = TextSample());
        col.AddChild(_visionSample = VisionSample());
        col.AddChild(_motionSample = MotionSample());
        return scroll;
    }

    private static PanelContainer SampleWell(Control content)
    {
        PanelContainer well = UiTheme.Well();
        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        pad.AddChild(content);
        well.AddChild(pad);
        return well;
    }

    /// <summary>One subtitle line on its plate over a mid-grey stand-in for the world, or the
    /// words "Subtitles are off".</summary>
    private Control SubtitleSample()
    {
        var world = new PanelContainer();
        var ground = new StyleBoxFlat { BgColor = UiTheme.Ash };
        ground.SetCornerRadiusAll(UiTheme.RadiusSm);
        ground.SetContentMarginAll(UiTheme.SpaceSm);
        world.AddThemeStyleboxOverride("panel", ground);

        var plate = new PanelContainer();
        var plateStyle = new StyleBoxFlat
        {
            BgColor = UiTheme.ScrimBg with { A = Mathf.Clamp(_settings.Current.SubtitleBackground, 0f, 1f) },
        };
        plateStyle.SetCornerRadiusAll(UiTheme.RadiusSm);
        plateStyle.SetContentMarginAll(UiTheme.SpaceXs);
        plateStyle.ContentMarginLeft = UiTheme.SpaceSm;
        plateStyle.ContentMarginRight = UiTheme.SpaceSm;
        plate.AddThemeStyleboxOverride("panel", plateStyle);
        world.AddChild(plate);

        var line = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        UiTheme.ApplyType(line, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        line.AddThemeColorOverride("font_color", UiTheme.Text);
        line.AddThemeColorOverride("font_outline_color", UiTheme.Keyline);
        line.AddThemeConstantOverride("outline_size", UiTheme.Space2xs);
        plate.AddChild(line);

        _subtitleLive = () =>
        {
            if (IsInstanceValid(line))
            {
                line.Text = Loc.T(_settings.Current.SubtitlesEnabled
                    ? "settings.preview.subtitle_named"
                    : "settings.preview.subtitle_off");
            }
        };
        _subtitleLive();
        return world;
    }

    private Control TextSample()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.LineGap);

        _textSample = UiTheme.Body(Loc.T("settings.preview.text"));
        _textSample.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _captionSample = UiTheme.Caption(Loc.T("settings.preview.caption"));
        _captionSample.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_textSample);
        box.AddChild(_captionSample);
        return SampleWell(box);
    }

    /// <summary>Sets the text sample at the size the slider is on, ahead of the interface.</summary>
    private void SizeTextSample()
    {
        if (_textSample == null || _captionSample == null ||
            !IsInstanceValid(_textSample) || !IsInstanceValid(_captionSample))
        {
            return;
        }

        float scale = _settings.Current.TextScale;
        _textSample.AddThemeFontSizeOverride("font_size", UiTheme.ScaledFontSize(UiTheme.BodyFontSize, scale));
        _captionSample.AddThemeFontSizeOverride("font_size", UiTheme.ScaledFontSize(UiTheme.CaptionFontSize, scale));
    }

    /// <summary>The colours that carry meaning, each with its word, as colour vision and contrast
    /// now draw them.</summary>
    private static Control VisionSample()
    {
        HFlowContainer row = UiTheme.FlowRow();
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.uncommon"), UiTheme.RarityColor(ItemRarity.Uncommon)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.rare"), UiTheme.RarityColor(ItemRarity.Rare)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.epic"), UiTheme.RarityColor(ItemRarity.Epic)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.good"), UiTheme.Good));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.bad"), UiTheme.Bad));
        return SampleWell(row);
    }

    /// <summary>A rule that keeps being drawn by its line of heat while motion is on, and is
    /// simply there when it is off; the words under it say which.</summary>
    private static Control MotionSample()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        Control wipe = UiOrnament.EmberWipe();
        box.AddChild(wipe);
        Label words = UiTheme.Caption(Loc.T(UiTheme.MotionEnabled ? "firstrun.motion_on" : "firstrun.motion_off"));
        words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(words);

        if (UiTheme.MotionEnabled)
        {
            // Freed with the sample; reduced motion rebuilds the sheet, so this is never left running.
            var again = new Timer { WaitTime = 2.4, Autostart = true, ProcessCallback = Timer.TimerProcessCallback.Idle };
            again.Timeout += () => UiOrnament.PlayEmberWipe(wipe);
            box.AddChild(again);
        }

        return SampleWell(box);
    }

    // --- Apply / leave ------------------------------------------------------

    private void Persist()
    {
        _settings.Apply();
        _settings.Save();
    }

    private void Done()
    {
        _settings.Save();
        System.Action? onDone = _onDone;
        _onDone = null;
        QueueFree();
        onDone?.Invoke();
    }

    /// <summary>Screenshot entry point: ends the opening fade.</summary>
    public void SettleForCapture()
    {
        if (_root != null)
        {
            UiFx.FadeIn(_root, 0f);
        }
    }

    /// <summary>Screenshot entry point: whether the five questions and their samples are built.</summary>
    public bool ReadyForCapture => _root != null && _scroll.IsInsideTree() && _textSample != null;
}
