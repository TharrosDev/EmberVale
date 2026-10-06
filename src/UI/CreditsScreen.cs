using Embervale.Core;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The credits, reachable from the title: a roll that climbs over its painting and hands the
/// player back to the title when the last line has gone. A shell screen like
/// <see cref="SettingsPanel"/>: the screen that opened it hides behind it and comes back on Back.
///
/// Back (Esc, B, or the button for a pointer) leaves at once. Holding accept hurries the roll, and
/// up and down, or the wheel, move it by hand. Under reduced motion it does not climb by itself: the player
/// reads it at their own pace and leaves with Back. The pace and its limits are
/// <see cref="ShellFrontRules.CreditsAdvance"/>.
/// </summary>
public partial class CreditsScreen : CanvasLayer
{
    private static readonly StringName Accept = "ui_accept";
    private static readonly StringName Up = "ui_up";
    private static readonly StringName Down = "ui_down";

    /// <summary>The roll, top to bottom: a heading key (or null) and the lines under it. This is
    /// a personal project built on other people's tools and free art; they are who is thanked.</summary>
    private static readonly (string? Heading, string[] Lines)[] Sections =
    {
        (null, new[] { "ending.credits.2" }),
        ("credits.made.heading", new[] { "credits.made.godot" }),
        ("credits.world.heading", new[] { "credits.world.kits", "credits.world.cast" }),
        ("credits.art.heading", new[] { "credits.art.paintings", "credits.art.glyphs" }),
        ("credits.type.heading", new[] { "credits.type.faces" }),
        (null, new[] { "ending.credits.3" }),
    };

    private System.Action? _onBack;
    private Control _root = null!;
    private VBoxContainer _roll = null!;
    private float _offset;
    private float _wheel;
    private bool _started;
    private bool _acceptArmed;
    private bool _heldForCapture;
    private bool _leaving;

    /// <summary>Opens the screen as a child of <paramref name="parent"/>, invoking
    /// <paramref name="onBack"/> when the player backs out or the roll ends.</summary>
    public static CreditsScreen Open(Node parent, System.Action? onBack = null)
    {
        var screen = new CreditsScreen { _onBack = onBack };
        parent.AddChild(screen);
        return screen;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 13; // above the main menu (11) and the slot panel (12), like the settings panel
        UiState.Open(this);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Build();
        UiFx.FadeIn(_root, UiTheme.DurationSlow);
    }

    public override void _ExitTree()
    {
        UiState.Close(this);
    }

    private void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.GuiInput += OnRootInput;
        AddChild(_root);

        ColorRect ground = UiTheme.Scrim(1f);
        ground.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(ground);
        _root.AddChild(UiTheme.Cover(UiTheme.Painting("credits")));

        // The roll is centred text over the whole painting, so the whole painting is dimmed.
        ColorRect dim = UiTheme.Scrim(UiTheme.HighContrast ? 0.94f : 0.6f);
        dim.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(dim);

        // Placed by hand each frame (its position is the scroll), so not in a container.
        // Hidden until that first placement: for one frame it would sit in the top-left corner.
        _roll = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _roll.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _root.AddChild(_roll);
        BuildRoll();

        // For a pointer only: with nothing focusable on the screen, accept is free to mean "faster".
        Button back = UiTheme.Action(Loc.T("common.back"), UiCue.Back);
        back.FocusMode = Control.FocusModeEnum.None;
        back.Position = new Vector2(UiTheme.SpaceLg, UiTheme.SpaceLg);
        back.Pressed += Back;
        _root.AddChild(back);

        var legend = new UiLegend();
        AddChild(legend);
        legend.Set(UiTheme.MotionEnabled
            ? new[]
            {
                new LegendEntry("ui_accept", Loc.T("credits.legend.faster")),
                new LegendEntry("ui_up", Loc.T("credits.legend.scroll"), "ui_down"),
                new LegendEntry("ui_cancel", Loc.T("common.back")),
            }
            : new[]
            {
                new LegendEntry("ui_up", Loc.T("credits.legend.scroll"), "ui_down"),
                new LegendEntry("ui_cancel", Loc.T("common.back")),
            });
    }

    private void BuildRoll()
    {
        _roll.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/ui/emblems/embervale_seal.png"),
            CustomMinimumSize = new Vector2(UiTheme.TitleSealSize, UiTheme.TitleSealSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _roll.AddChild(Centred(UiTheme.Display(Loc.T("ending.credits.1"), UiTheme.Accent)));

        foreach ((string? heading, string[] lines) in Sections)
        {
            _roll.AddChild(new Control
            {
                CustomMinimumSize = new Vector2(0f, UiTheme.SpaceXl),
                MouseFilter = Control.MouseFilterEnum.Ignore, // the wheel over a gap still reaches the roll
            });
            if (heading != null)
            {
                _roll.AddChild(Centred(UiTheme.Header(Loc.T(heading))));
            }

            foreach (string line in lines)
            {
                _roll.AddChild(Centred(heading != null ? UiTheme.Body(Loc.T(line)) : UiTheme.Prose(Loc.T(line))));
            }
        }
    }

    /// <summary>The wheel moves the roll a row a notch: under reduced motion nothing else a
    /// pointer has would.</summary>
    private void OnRootInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelDown)
            {
                _wheel += UiTheme.ControlHeight;
            }
            else if (button.ButtonIndex == MouseButton.WheelUp)
            {
                _wheel -= UiTheme.ControlHeight;
            }
        }
    }

    private static Label Centred(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    public override void _Process(double delta)
    {
        if (_leaving)
        {
            return;
        }

        if (Godot.Input.IsActionJustPressed(UiLive.Pause) ||
            Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            UiAudio.Play(UiCue.Back);
            Back();
            return;
        }

        Vector2 view = _root.Size;
        float width = Mathf.Min(UiTheme.CreditsColumnWidth, view.X - (UiTheme.SpaceLg * 2f));
        float start = ShellFrontRules.CreditsStart(view.Y);
        float end = ShellFrontRules.CreditsEnd(_roll.GetCombinedMinimumSize().Y, view.Y);
        if (!_started)
        {
            _started = true;
            _offset = start;
        }

        // The press that opened the credits is still down; it does not also hurry them.
        bool accept = Godot.Input.IsActionPressed(Accept);
        _acceptArmed |= !accept;

        bool auto = UiTheme.MotionEnabled && !_heldForCapture;
        if (!_heldForCapture)
        {
            _offset = ShellFrontRules.CreditsAdvance(
                _offset + _wheel, (float)delta, auto, accept && _acceptArmed, Godot.Input.GetAxis(Up, Down), start, end);
        }

        _wheel = 0f;
        var place = new Vector2(Mathf.Round((view.X - width) * 0.5f), Mathf.Round(view.Y - _offset));
        if (_roll.Position != place || _roll.Size.X != width)
        {
            _roll.Position = place;
            _roll.Size = new Vector2(width, 0f); // as tall as its lines need at this width
            _roll.Visible = true;
        }

        if (auto && _offset >= end)
        {
            Back(); // the last line has left the top: back to the title
        }
    }

    private void Back()
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        System.Action? onBack = _onBack;
        QueueFree();
        onBack?.Invoke();
    }

    /// <summary>Screenshot entry point: holds the roll still at <paramref name="fraction"/> of its
    /// way from its opening position (0) to its last lines in view (1).</summary>
    public void SetScrollForCapture(float fraction)
    {
        _heldForCapture = true;
        _started = true;
        float viewHeight = _root.Size.Y;
        float start = ShellFrontRules.CreditsStart(viewHeight);
        float last = Mathf.Max(start, _roll.GetCombinedMinimumSize().Y);
        _offset = Mathf.Lerp(start, last, Mathf.Clamp(fraction, 0f, 1f));
        UiFx.FadeIn(_root, 0f);
    }

    /// <summary>How far the roll has climbed, in px, and how many lines it holds.</summary>
    public float OffsetForCapture => _offset;

    public int LineCountForCapture => _roll.GetChildCount();
}
