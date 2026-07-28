using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Races;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The prologue (Phase 33A): the beat between character creation and the Ember Crown. A black field
/// with narration cards fading through the premise — the dying world, the six who fell, the seventh
/// who remains — closing on the player's own name before the world is revealed underneath.
///
/// It exists so a new game <em>starts</em> instead of merely beginning: the world is already built
/// and streaming behind this screen, so when the last card fades the player is standing in the town
/// with nothing to load. Input is held through <see cref="UiState"/> (the same lock a modal panel
/// uses) while leaving the mouse captured, so no cursor appears over the narration.
///
/// Skippable at any point with the interact/attack key — a veteran must never be made to sit
/// through it — and it plays only on New Game, never on a load.
/// </summary>
public partial class OpeningSequence : CanvasLayer
{
    /// <summary>The narration, as <c>Loc</c> keys. The last card is formatted with the character's
    /// name, so the prologue ends on who the player just made rather than on lore.</summary>
    private static readonly string[] Cards =
    {
        "opening.card1",
        "opening.card2",
        "opening.card3",
        "opening.card4",
        "opening.card5",
    };

    private ColorRect _backdrop = null!;
    private Label _text = null!;
    private Label _skipHint = null!;
    private float _elapsed;
    private bool _running;
    private string _characterName = string.Empty;

    /// <summary>Raised (via the EventBus) when the sequence ends, whether it played out or was
    /// skipped. The bootstrap hands control to the player on this.</summary>
    public override void _Ready()
    {
        // Above the loading screen (20): the prologue is the last thing over the world.
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        Build();
    }

    /// <summary>Starts the prologue for <paramref name="profile"/>'s character.</summary>
    public void Play(CharacterProfile profile)
    {
        _characterName = string.IsNullOrWhiteSpace(profile.CharacterName)
            ? Loc.T("opening.nameless")
            : profile.CharacterName;

        _elapsed = 0f;
        _running = true;
        Visible = true;
        _backdrop.Color = new Color(0.02f, 0.02f, 0.03f, 1f);
        _skipHint.Text = Loc.TF("opening.skip", GameInput.PromptLabel(GameInput.Interact));
        UiState.Open(this);
        Refresh(OpeningTimeline.At(0f, Cards.Length));
    }

    public override void _Process(double delta)
    {
        if (!_running)
        {
            return;
        }

        // Interact/attack skip; Esc deliberately does not, or one press would both end the prologue
        // and open the pause menu behind it.
        if (Godot.Input.IsActionJustPressed(GameInput.Interact) ||
            Godot.Input.IsActionJustPressed(GameInput.Attack))
        {
            Finish();
            return;
        }

        _elapsed += (float)delta;
        OpeningFrame frame = OpeningTimeline.At(_elapsed, Cards.Length);
        if (frame.Finished)
        {
            Finish();
            return;
        }

        Refresh(frame);
    }

    /// <summary>Ends the sequence and hands the world to the player.</summary>
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
        // prologue (or an alt-tab) could have, and the player is about to be given the camera.
        if (GameManager.Instance is { IsPlaying: true } || !UiState.MenuOpen)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
        }

        EventBus.Instance?.Publish(new OpeningFinishedEvent());
    }

    private void Refresh(OpeningFrame frame)
    {
        string key = Cards[Mathf.Clamp(frame.CardIndex, 0, Cards.Length - 1)];

        // The closing card names the character; the rest are plain narration. TF is harmless on a
        // string with no placeholder, so every card goes through the same path.
        _text.Text = Loc.TF(key, _characterName);
        _text.Modulate = new Color(1f, 1f, 1f, frame.Alpha);

        // The skip hint only appears once the first card has settled, so it never competes with the
        // opening line for attention.
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
