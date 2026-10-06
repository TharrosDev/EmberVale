using Embervale.Core;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The shared narration-card renderer (Phase 33A's prologue, generalised in 33D for the slice's
/// closing card). A black field, one card at a time fading in/holding/out, a skip hint, and an input
/// lock that leaves the mouse captured so no cursor sits over the text. The two endings play over
/// their painting (<see cref="ShellSessionRules.NarrationBackdrop"/>), which comes up slowly out of
/// the black and goes back into it with the last card.
///
/// Subclasses supply the script and decide what to announce when it ends; the pacing itself lives in
/// the pure <see cref="OpeningTimeline"/> and is unit-tested. Extracting this rather than copying it
/// means the opening and the ending can never drift apart in feel — which is exactly the kind of
/// seam a slice is judged on.
///
/// ⚠️ <b>The timeline never waits on the player.</b> Skipping is a hold (<see cref="HoldRing"/>),
/// and the pause prompt stops the clock, but both only ever happen on a press: with no input at all
/// the cards play out and the sequence finishes, which is what the <c>--story</c> gate relies on
/// when it runs every sequence at 25x and presses nothing.
/// </summary>
public abstract partial class NarrationSequence : CanvasLayer
{
    /// <summary>How much of the hint shows while nothing is held: it must never compete with the card.</summary>
    private const float HintAlpha = 0.55f;

    // The world owner of the pause prompt. The sequence itself is registered as a cinematic lock
    // that leaves the world running; pausing it has to hold the world too, under its own name.
    private readonly object _pauseOwner = new();

    private ColorRect _backdrop = null!;
    private TextureRect _painting = null!;
    private ColorRect _wash = null!;
    private Label _text = null!;
    private HBoxContainer _hint = null!;
    private HoldRing _skipRing = null!;
    private SessionPrompt? _pausePrompt;
    private string[] _cards = System.Array.Empty<string>();
    private string _argument = string.Empty;
    private float _elapsed;
    private float _paintingAlphaShown = -1f;
    private float _hintAlphaShown = -1f;
    private bool _running;
    private bool _skipHeld;
    private bool _paused;
    private bool _hintForPad;
    private bool _hintForPresses;
    private bool _frozenForCapture;
    private Godot.Input.MouseModeEnum _mouseBeforePause;

    /// <summary>Whether the sequence is currently playing.</summary>
    public bool IsPlaying => _running;

    public override void _Ready()
    {
        // Above the loading screen (20): narration is the last thing over the world.
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        Build();
        OnReady();
    }

    /// <summary>Subclass hook for event subscriptions. Pair with <c>_ExitTree</c>.</summary>
    protected virtual void OnReady()
    {
    }

    /// <summary>Called once the last card has faded (or the player skipped). Subclasses publish
    /// whatever the rest of the game is waiting on.</summary>
    protected abstract void OnSequenceFinished();

    public override void _Notification(int what)
    {
        // Here and not in _ExitTree: every subclass overrides that for its own subscriptions. A
        // session torn down under the pause prompt must not leave the world held by it.
        if (what == NotificationExitTree && _paused)
        {
            _paused = false;
            UiState.Close(_pauseOwner);
        }
    }

    /// <summary>
    /// Plays <paramref name="cards"/> (a list of <c>Loc</c> keys) in order. <paramref name="argument"/>
    /// is formatted into every card, so a script can name the character without the renderer knowing
    /// what a character is.
    /// </summary>
    protected void PlayCards(string[] cards, string argument)
    {
        if (cards.Length == 0)
        {
            return;
        }

        _cards = cards;
        _argument = argument;
        _elapsed = 0f;
        _running = true;
        _skipHeld = false;
        Visible = true;

        // The card's measure, or what a narrow view leaves of it.
        _text.CustomMinimumSize = new Vector2(
            ShellSessionRules.NarrationWidth(GetViewport().GetVisibleRect().Size.X, UiTheme.NarrationMeasure, UiTheme.SpaceXl), 0f);
        ShowBackdrop(ShellSessionRules.NarrationBackdrop(cards));
        BuildHint();

        // Cinematic lock, not a menu: the prologue plays over the already-built world (see
        // the application root), so it suspends the player's controls without stopping the simulation.
        UiState.Open(this, pausesWorld: false);
        Refresh(OpeningTimeline.At(0f, _cards.Length));
    }

    public override void _Process(double delta)
    {
        if (!_running || _frozenForCapture)
        {
            return;
        }

        if (_paused)
        {
            // Esc / B answers the prompt with Resume. The clock is stopped until it does.
            if (Godot.Input.IsActionJustPressed(UiLive.Pause) || Godot.Input.IsActionJustPressed(UiLive.UiCancel))
            {
                UiAudio.Play(UiCue.Back);
                Resume();
            }

            return;
        }

        // Esc used to do nothing here: the pause menu stands down while a narration holds the
        // screen. It now stops the cards and asks.
        if (Godot.Input.IsActionJustPressed(UiLive.Pause))
        {
            Pause();
            return;
        }

        PollSkip();
        if (!_running)
        {
            return; // the hold closed (or, with holds as presses, the press did)
        }

        if (_hintForPad != InputDevice.GamepadActive || _hintForPresses != UiFx.HoldsToPresses)
        {
            BuildHint(); // the glyphs are a snapshot of one device
        }

        _elapsed += (float)delta;
        OpeningFrame frame = OpeningTimeline.At(_elapsed, _cards.Length);
        if (frame.Finished)
        {
            Finish();
            return;
        }

        Refresh(frame);
    }

    /// <summary>
    /// Interact or attack, held, skips. Esc deliberately does not. The hold only starts on a fresh
    /// press, so the click that began the game cannot run into the prologue's skip, and it is only
    /// ever driven from here: the ring fills on its own clock and calls <see cref="Finish"/>, and
    /// the cards never wait for it.
    /// </summary>
    private void PollSkip()
    {
        if (!_skipHeld)
        {
            if (Godot.Input.IsActionJustPressed(UiLive.Interact) || Godot.Input.IsActionJustPressed(UiLive.Attack))
            {
                _skipHeld = true;
                _skipRing.Press();
            }
        }
        else if (!Godot.Input.IsActionPressed(UiLive.Interact) && !Godot.Input.IsActionPressed(UiLive.Attack))
        {
            _skipHeld = false;
            _skipRing.Release();
        }
    }

    private void Finish()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _skipHeld = false;
        _skipRing.Release();
        ClosePausePrompt();
        Visible = false;
        UiState.Close(this);

        // Re-capture the mouse: the sequence never released it, but a panel opened during the
        // narration (or an alt-tab) could have, and the player is about to be given the camera.
        if (GameManager.Instance is { IsPlaying: true } || !UiState.MenuOpen)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
        }

        OnSequenceFinished();
    }

    // --- Pause prompt -------------------------------------------------------

    private void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _skipHeld = false;
        _skipRing.Release();
        _mouseBeforePause = Godot.Input.MouseMode;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        UiState.Open(_pauseOwner); // holds the world as well as the cards
        UiAudio.Play(UiCue.Open);

        // Resume first: the default answer is the one that loses nothing.
        _pausePrompt = SessionPrompt.Open(
            this, Loc.T("narration.paused"), string.Empty,
            new SessionPrompt.Choice(Loc.T("narration.resume"), UiCue.Back, Resume, Focused: true),
            new SessionPrompt.Choice(Loc.T("narration.skip"), UiCue.Confirm, Finish));
    }

    private void Resume()
    {
        if (!_paused)
        {
            return;
        }

        ClosePausePrompt();
        Godot.Input.MouseMode = _mouseBeforePause;
    }

    /// <summary>Takes the prompt down and lets the world go. Safe to call when it is not up.</summary>
    private void ClosePausePrompt()
    {
        if (!_paused)
        {
            return;
        }

        _paused = false;
        _pausePrompt?.Close();
        _pausePrompt = null;
        UiState.Close(_pauseOwner);
    }

    // --- Drawing ------------------------------------------------------------

    private void Refresh(OpeningFrame frame)
    {
        string key = _cards[Mathf.Clamp(frame.CardIndex, 0, _cards.Length - 1)];

        // TF is harmless on a string with no placeholder, so every card goes through the same path
        // whether or not it wants the argument.
        _text.Text = Loc.TF(key, _argument);
        _text.Modulate = new Color(1f, 1f, 1f, frame.Alpha);

        // The skip hint tracks the card's fade so it never competes with the opening line, and
        // comes up to full while the ring is filling so the hold can be read.
        float hint = _skipRing.Holding || _skipRing.Progress > 0f ? 1f : Mathf.Min(frame.Alpha, HintAlpha);
        if (!Mathf.IsEqualApprox(hint, _hintAlphaShown))
        {
            _hintAlphaShown = hint;
            _hint.Modulate = new Color(1f, 1f, 1f, hint);
        }

        if (_painting.Visible)
        {
            float painting = ShellSessionRules.BackdropAlpha(
                _elapsed, OpeningTimeline.Duration(_cards.Length), OpeningTimeline.FadeSeconds);
            if (!Mathf.IsEqualApprox(painting, _paintingAlphaShown))
            {
                _paintingAlphaShown = painting;
                _painting.Modulate = new Color(1f, 1f, 1f, painting);
                _wash.Modulate = new Color(1f, 1f, 1f, painting);
            }
        }
    }

    /// <summary>Puts <paramref name="path"/>'s painting under the cards, or takes any painting
    /// away. A painting that cannot be loaded leaves the plain black field.</summary>
    private void ShowBackdrop(string? path)
    {
        Texture2D? texture = path != null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        _painting.Texture = texture;
        _painting.Visible = texture != null;
        _wash.Visible = texture != null;
        _paintingAlphaShown = -1f;
        _painting.Modulate = new Color(1f, 1f, 1f, 0f);
        _wash.Modulate = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>
    /// The row under the card: the key and "Hold to skip" with its ring (or just "Skip" when holds
    /// are presses), then the key that pauses. Rebuilt when the device changes, because a glyph is
    /// a snapshot.
    /// </summary>
    private void BuildHint()
    {
        _hintForPad = InputDevice.GamepadActive;
        _hintForPresses = UiFx.HoldsToPresses;

        // The ring is kept across rebuilds: it may be mid-hold.
        if (_skipRing.GetParent() == _hint)
        {
            _hint.RemoveChild(_skipRing);
        }

        UiTheme.ClearChildren(_hint);
        _hint.AddChild(Centred(UiGlyph.For(GameInput.Interact)));
        _hint.AddChild(HintLabel(Loc.T(_hintForPresses ? "narration.skip" : "narration.skip_hold")));
        _skipRing.Visible = !_hintForPresses;
        _hint.AddChild(_skipRing);
        _hint.AddChild(new Control { CustomMinimumSize = new Vector2(UiTheme.SpaceSm, 0f), MouseFilter = Control.MouseFilterEnum.Ignore });
        _hint.AddChild(Centred(UiGlyph.For(GameInput.Pause)));
        _hint.AddChild(HintLabel(Loc.T("narration.pause")));
    }

    private static Control Centred(Control glyph)
    {
        glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return glyph;
    }

    private static Label HintLabel(string text)
    {
        Label label = UiTheme.Caption(text, UiTheme.Dim);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private void Build()
    {
        _backdrop = UiTheme.Scrim(1f);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);

        // The painting, and a wash over it so a line of bone text holds its contrast on a bright sky.
        _painting = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _painting.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_painting);

        _wash = UiTheme.Scrim(0.55f);
        _wash.MouseFilter = Control.MouseFilterEnum.Ignore;
        _wash.Visible = false;
        AddChild(_wash);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(centre);

        _text = UiTheme.Header(string.Empty);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(UiTheme.NarrationMeasure, 0f);
        UiTheme.ApplyType(_text, UiTheme.FontRole.Serif, UiTheme.TitleFontSize);
        UiTheme.HudInk(_text); // a keyline, for the cards that play over a painting
        centre.AddChild(_text);

        _hint = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hint.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hint.GrowHorizontal = Control.GrowDirection.Both;
        _hint.GrowVertical = Control.GrowDirection.Begin;
        _hint.OffsetBottom = -UiTheme.SpaceXl;
        _hint.OffsetTop = -UiTheme.SpaceXl;
        AddChild(_hint);

        _skipRing = UiFx.HoldRing(Finish);
        _hint.AddChild(_skipRing);
    }

    // --- Capture hooks ------------------------------------------------------

    /// <summary>Capture hook: shows <paramref name="cards"/> as they stand <paramref name="elapsed"/>
    /// seconds in and holds there. Nothing is announced when it ends (<see cref="EndForCapture"/>),
    /// so a harness can photograph an ending without the save recording that the game was finished.</summary>
    public void ShowCardForCapture(string[] cards, string argument, float elapsed)
    {
        PlayCards(cards, argument);
        if (!_running)
        {
            return;
        }

        _frozenForCapture = true;
        _elapsed = elapsed;
        Refresh(OpeningTimeline.At(elapsed, _cards.Length));
    }

    /// <summary>Capture hook: opens the Resume / Skip prompt over the held card.</summary>
    public void PauseForCapture()
    {
        if (_running)
        {
            Pause();
        }
    }

    /// <summary>Capture hook: takes the sequence down without calling <see cref="OnSequenceFinished"/>.</summary>
    public void EndForCapture()
    {
        if (!_running)
        {
            return;
        }

        bool wasPaused = _paused;
        ClosePausePrompt();
        if (wasPaused)
        {
            Godot.Input.MouseMode = _mouseBeforePause;
        }

        _frozenForCapture = false;
        _running = false;
        Visible = false;
        UiState.Close(this);
    }

    /// <summary>Whether a painting is drawn under the card. Read by the screenshot harness.</summary>
    public bool BackdropShownForCapture => _running && _painting.Visible && _painting.Texture != null;

    /// <summary>Whether the Resume / Skip prompt is up. Read by the screenshot harness.</summary>
    public bool PausedForCapture => _paused;
}
