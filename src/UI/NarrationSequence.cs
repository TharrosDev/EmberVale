using Embervale.Core;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The shared narration-card renderer (Phase 33A's prologue, generalised in 33D for the slice's
/// closing card). A black field, one card at a time fading in/holding/out, a skip hint, and an input
/// lock that leaves the mouse captured so no cursor sits over the text.
///
/// Subclasses supply the script and decide what to announce when it ends; the pacing itself lives in
/// the pure <see cref="OpeningTimeline"/> and is unit-tested. Extracting this rather than copying it
/// means the opening and the ending can never drift apart in feel — which is exactly the kind of
/// seam a slice is judged on.
/// </summary>
public abstract partial class NarrationSequence : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private Label _text = null!;
    private Label _skipHint = null!;
    private string[] _cards = System.Array.Empty<string>();
    private string _argument = string.Empty;
    private float _elapsed;
    private bool _running;

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
        Visible = true;
        _skipHint.Text = Loc.TF("opening.skip", GameInput.PromptLabel(GameInput.Interact));
        UiState.Open(this);
        Refresh(OpeningTimeline.At(0f, _cards.Length));
    }

    public override void _Process(double delta)
    {
        if (!_running)
        {
            return;
        }

        // Interact/attack skip; Esc deliberately does not, or one press would both end the narration
        // and open the pause menu behind it.
        if (Godot.Input.IsActionJustPressed(GameInput.Interact) ||
            Godot.Input.IsActionJustPressed(GameInput.Attack))
        {
            Finish();
            return;
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

    private void Finish()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
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

    private void Refresh(OpeningFrame frame)
    {
        string key = _cards[Mathf.Clamp(frame.CardIndex, 0, _cards.Length - 1)];

        // TF is harmless on a string with no placeholder, so every card goes through the same path
        // whether or not it wants the argument.
        _text.Text = Loc.TF(key, _argument);
        _text.Modulate = new Color(1f, 1f, 1f, frame.Alpha);

        // The skip hint tracks the card's fade so it never competes with the opening line.
        _skipHint.Modulate = new Color(1f, 1f, 1f, Mathf.Min(frame.Alpha, 0.55f));
    }

    private void Build()
    {
        _backdrop = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f) };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(centre);

        _text = UiTheme.Header(string.Empty);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(680f, 0f);
        _text.AddThemeFontSizeOverride("font_size", UiTheme.TitleFontSize);
        centre.AddChild(_text);

        _skipHint = UiTheme.Caption(string.Empty, UiTheme.Dim);
        _skipHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _skipHint.GrowHorizontal = Control.GrowDirection.Both;
        _skipHint.OffsetTop = -64f;
        _skipHint.OffsetBottom = -40f;
        _skipHint.HorizontalAlignment = HorizontalAlignment.Center;
        _skipHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_skipHint);
    }
}
