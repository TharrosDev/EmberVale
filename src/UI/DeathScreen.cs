using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The screen between the player's death and what they do about it: the frame darkens, one line
/// is set over it, and after a breath the player chooses to rise where the realm puts them or to
/// load their last save.
///
/// Built with the session's shell (<c>UICompositionRoot.Death</c>) and driven by <c>PlayerHost</c>,
/// which still respawns the player in the same call that reports the death: nothing about dying
/// changes underneath, this only holds the world still (a pausing modal through
/// <see cref="UiState"/>) until the player has looked at it.
///
/// ⚠️ <b><see cref="Begin"/> does nothing when nobody is at the controls</b>
/// (<see cref="ShellSessionRules.Unattended"/>): a headless gate, a probe or a capture run that
/// kills the player must get the same-frame respawn it always got, not a modal that waits for a
/// press that never comes. The capture harness asks through <see cref="BeginForCapture"/>.
/// </summary>
public partial class DeathScreen : CanvasLayer
{
    private static bool? _unattended;

    private Control? _root;
    private VBoxContainer _options = null!;
    private Button _rise = null!;
    private Label _status = null!;
    private UiLegend? _legend;
    private ulong _shownAtMsec;
    private bool _queued;
    private bool _optionsShown;
    private bool _holdOptions;

    public override void _Ready()
    {
        // A death screen holds the world still and has to keep answering input while it does.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 9; // above the HUD and the panels, below the pause menu (10)
        Visible = false;
        SetProcess(false); // asleep until a death; Present wakes it and Dismiss puts it back
    }

    public override void _ExitTree()
    {
        // A load from this screen destroys the session with the screen still up.
        if (Visible)
        {
            UiState.Close(this);
        }
    }

    /// <summary>Whether the screen is up.</summary>
    public bool Active => Visible;

    /// <summary>Whether Rise and Load last save are on screen and taking input.</summary>
    public bool OptionsShown => _optionsShown;

    /// <summary>
    /// Called when the player has died, before any respawn. Shows the screen at the end of the
    /// frame, once the death has finished being reported: the caller is inside the event that
    /// reports it, and pausing the tree from there would land in the middle of a hit.
    /// </summary>
    public void Begin()
    {
        if (!Unattended())
        {
            Queue(holdOptions: false);
        }
    }

    /// <summary>Capture hook: <see cref="Begin"/> for a run the unattended rule would skip.
    /// With <paramref name="holdOptions"/> the choices stay hidden until
    /// <see cref="ShowOptionsForCapture"/>, so the hold can be photographed on any machine.</summary>
    public void BeginForCapture(bool holdOptions = true) => Queue(holdOptions);

    /// <summary>Capture hook: ends the hold and shows the choices now.</summary>
    public void ShowOptionsForCapture()
    {
        if (Visible && !_optionsShown)
        {
            _holdOptions = false;
            ShowOptions();
        }
    }

    /// <summary>Capture hook: takes the screen down as Rise would.</summary>
    public void EndForCapture() => Dismiss();

    private static bool Unattended() => _unattended ??= ShellSessionRules.Unattended(
        DisplayServer.GetName(), OS.GetEnvironment("EMBERVALE_USER_DIR"), OS.GetCmdlineUserArgs().Length);

    /// <summary>There is a session to show it over, someone is playing it, and nothing else has
    /// the screen: a death under a conversation or a cinematic lock keeps today's plain respawn.</summary>
    private bool CanPresent() =>
        IsInsideTree() && !Visible &&
        GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen &&
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter _);

    private void Queue(bool holdOptions)
    {
        if (_queued || !CanPresent())
        {
            return;
        }

        _queued = true;
        _holdOptions = holdOptions;
        Callable.From(Present).CallDeferred();
    }

    private void Present()
    {
        if (!IsInstanceValid(this))
        {
            return;
        }

        _queued = false;
        if (!CanPresent())
        {
            return;
        }

        Build();
        Visible = true;
        _optionsShown = false;
        _shownAtMsec = Time.GetTicksMsec();
        UiState.Open(this);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;

        // The frame darkens rather than cuts. UiFx runs while the tree is paused, which it now is.
        UiFx.FadeIn(_root!, UiTheme.DurationDeath);
        SetProcess(true);
    }

    /// <summary>Built for each death, not once: text size, contrast and the font can all have
    /// changed since the last one, and it costs a handful of controls.</summary>
    private void Build()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Min(UiTheme.DeathSheetWidth, view.X - (UiChromeRules.Gutter(view.X) * 2f));
        // Dark enough to be one surface: at the old 0.82 the HUD read through it as a second screen.
        (Control root, VBoxContainer col) = UiTheme.Sheet(width, UiTheme.HighContrast ? 0.97f : UiTheme.DeathScrim, centred: true);
        col.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        _root = root;
        AddChild(root);

        // Bone, not ember: the line is the one thing on the screen and does not need to shout.
        // The rule drawn under it is the screen's one ornament.
        var heading = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Label line = UiTheme.Display(Loc.T("death.line"), UiTheme.Text);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.AddChild(line);
        heading.AddChild(UiOrnament.EmberWipe(seconds: UiTheme.DurationDeath));
        col.AddChild(heading);

        _options = new VBoxContainer { Visible = false };
        _options.AddThemeConstantOverride("separation", UiTheme.SessionEntryGap);
        col.AddChild(_options);

        _rise = UiTheme.SessionAction(Loc.T("death.rise"), UiCue.Confirm);
        _rise.Pressed += Dismiss;
        _options.AddChild(_rise);

        // Greyed with its reason when there is nothing to load: a new game that has not saved yet.
        bool hasSave = SaveManager.Instance is { } saves && saves.SaveExists(saves.ActiveSlot);
        Button load = UiTheme.SessionAction(Loc.T("death.load"));
        load.Disabled = !hasSave;
        load.Pressed += LoadLastSave;
        _options.AddChild(load);

        _status = UiTheme.Caption(hasSave ? string.Empty : Loc.T("death.no_save"));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.Visible = !hasSave;
        _options.AddChild(_status);

        _legend = new UiLegend { Visible = false };
        AddChild(_legend);
        _legend.Set(new[]
        {
            new LegendEntry("ui_accept", Loc.T("session.legend.select")),
            new LegendEntry("ui_cancel", Loc.T("death.rise")),
        });
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            SetProcess(false);
            return;
        }

        if (!_optionsShown)
        {
            // Timed on the wall clock: the tree is paused, and a hit-stop may still be holding the
            // time scale near zero.
            double elapsed = (Time.GetTicksMsec() - _shownAtMsec) / 1000d;
            if (!_holdOptions && ShellSessionRules.DeathAcceptsInput(elapsed))
            {
                ShowOptions();
            }

            return;
        }

        // Esc / B rises. At the end of the frame, not here: Esc is also the pause action, and the
        // pause menu, which is polled after this screen, must still find a menu open on this press.
        if (Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            UiAudio.Play(UiCue.Back);
            Callable.From(Dismiss).CallDeferred();
        }
    }

    private void ShowOptions()
    {
        _optionsShown = true;
        UiFx.FadeIn(_options);
        if (_legend != null)
        {
            _legend.Visible = true;
        }

        _rise.GrabFocus();
    }

    private void Dismiss()
    {
        if (!IsInstanceValid(this) || !Visible)
        {
            return;
        }

        // Instant, like every menu closing: the player asked for the game back.
        Visible = false;
        _optionsShown = false;
        SetProcess(false);
        UiState.Close(this);
        if (GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>
    /// Reloads the session's own save through the pause menu's checkpoint route
    /// (<see cref="PauseMenu.RequestLoad"/>), which rebuilds the session and this screen with it.
    /// The screen stays up until that happens, so the world is never let go in between; if the
    /// save is refused it says so and Rise is still there.
    /// </summary>
    private void LoadLastSave()
    {
        foreach (Node sibling in GetParent().GetChildren())
        {
            if (sibling is PauseMenu pause && pause.RequestLoad())
            {
                return;
            }
        }

        _status.Text = Loc.T("pause.load_refused");
        _status.AddThemeColorOverride("font_color", UiTheme.Bad);
        _status.Visible = true;
        _rise.GrabFocus();
    }
}
