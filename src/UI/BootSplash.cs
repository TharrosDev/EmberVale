using Embervale.Core;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The first thing on screen: black, the seal coming up out of it, and "Press any button". The
/// button pressed is also how the game learns which device the player is holding, so the title
/// under it opens with the right glyphs.
///
/// Shown once per process, by <see cref="MainMenu"/>, and only to a person
/// (<see cref="Wanted"/>): a headless run, an isolated user folder or any argument after
/// <c>--</c> skips it, because it waits for input no tool will send. It is a child of the menu, so
/// anything that dismisses the title takes the splash with it.
/// </summary>
public partial class BootSplash : CanvasLayer
{
    /// <summary>Seconds the seal takes to come up, and the prompt waits before following it.</summary>
    private const float SealSeconds = 0.9f;

    /// <summary>Presses are ignored this long after opening: the key that launched the game, or a
    /// click meant for the desktop, is not an answer.</summary>
    private const ulong DeafMsec = 300;

    private static bool _shownThisProcess;

    private System.Action? _onDone;
    private Control _root = null!;
    private Control _seal = null!;
    private Control _prompt = null!;
    private ColorRect _black = null!;
    private ulong _openedMsec;
    private bool _leaving;

    /// <summary>Whether the splash should open now: a person is at the machine and it has not
    /// been shown since the process started (returning to the title does not replay it).</summary>
    public static bool Wanted =>
        !_shownThisProcess && ShellFrontRules.Attended(
            DisplayServer.GetName() == "headless",
            !string.IsNullOrWhiteSpace(OS.GetEnvironment("EMBERVALE_USER_DIR")),
            OS.GetCmdlineUserArgs().Length);

    /// <summary>Opens the splash over <paramref name="parent"/>. <paramref name="onDone"/> runs on
    /// the press, before the splash fades, so what it reveals is already there.</summary>
    public static BootSplash Open(Node parent, System.Action? onDone)
    {
        _shownThisProcess = true;
        var splash = new BootSplash { _onDone = onDone };
        parent.AddChild(splash);
        return splash;
    }

    /// <summary>Whether the prompt is up and a press would be taken.</summary>
    public bool WaitingForCapture => !_leaving && _prompt.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 14; // over the title (11) and every screen it opens (12, 13)
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        _openedMsec = Time.GetTicksMsec();
        Build();
    }

    private void Build()
    {
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _black = UiTheme.Scrim(1f);
        _black.MouseFilter = Control.MouseFilterEnum.Stop;
        _root.AddChild(_black);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(centre);

        // As large as the screen lets it be and still clear the prompt under it.
        float seal = Mathf.Min(
            UiTheme.SplashSealSize, GetViewport().GetVisibleRect().Size.Y * UiTheme.SplashSealShare);
        _seal = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/ui/emblems/embervale_seal.png"),
            CustomMinimumSize = new Vector2(seal, seal),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        centre.AddChild(_seal);

        // Under the seal, in the lower third, on its own anchors so the seal stays dead centre.
        Label prompt = UiTheme.Body(Loc.T("splash.press_any"));
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.MouseFilter = Control.MouseFilterEnum.Ignore;
        prompt.AnchorLeft = 0f;
        prompt.AnchorRight = 1f;
        prompt.AnchorTop = 0.8f;
        prompt.AnchorBottom = 0.8f;
        prompt.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(prompt);
        _prompt = prompt;

        UiFx.FadeIn(_seal, SealSeconds);
        UiFx.FadeIn(_prompt, UiTheme.DurationSlow, SealSeconds);
    }

    public override void _Input(InputEvent @event)
    {
        bool press = @event is InputEventKey { Pressed: true, Echo: false }
            or InputEventJoypadButton { Pressed: true }
            or InputEventMouseButton { Pressed: true };
        if (!press)
        {
            return;
        }

        // Taken here so nothing under the splash acts on it, which also keeps it from the device
        // tracker: tell that ourselves.
        InputDevice.Observe(@event);
        GetViewport().SetInputAsHandled();
        if (_leaving || Time.GetTicksMsec() - _openedMsec < DeafMsec)
        {
            return;
        }

        Leave();
    }

    private void Leave()
    {
        _leaving = true;

        // What the press brought up is live from here: the fading black takes no more presses
        // and lets the pointer through.
        SetProcessInput(false);
        _black.MouseFilter = Control.MouseFilterEnum.Ignore;
        UiAudio.Play(UiCue.Confirm);
        System.Action? onDone = _onDone;
        _onDone = null;
        onDone?.Invoke();

        // An exit that reveals: the black lifts off whatever the press brought up.
        UiFx.FadeOut(_root, QueueFree, UiTheme.DurationSlow);
    }

    /// <summary>Screenshot entry point: the seal and the prompt at rest, with no fade in flight.</summary>
    public void SettleForCapture()
    {
        UiFx.FadeIn(_seal, 0f);
        UiFx.FadeIn(_prompt, 0f);
    }
}
