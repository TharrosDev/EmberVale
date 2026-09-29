using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Screen feedback for the player's combat states (Phase 29D, deepened): a brief full-screen colour
/// flash plus a short word, distinct per state — crit gold, riposte hot gold, backstab dusk violet,
/// block steel, stagger red, parry bright, guard broken dried-blood, and the player breaking an
/// enemy's poise in pale brass — so a hit reads at a glance. It reads one outcome per blow from
/// <see cref="HitConfirmedEvent"/> (the <see cref="CombatFeedbackDirector"/> names the blow), maps it
/// to a state with <see cref="CombatFeedbackFx.ForOutcome"/>, and scales the flash by the player's
/// combat flash setting (<see cref="CombatComfort.ScreenFlash"/>, which Reduced Motion caps).
///
/// <para>A <see cref="FlashGate"/> keeps the flash under three a second so a run of blocks or crits
/// cannot strobe; a stronger state always gets through. The word is never suppressed by the flash
/// setting — turning the flash off leaves the information. It also hosts the floating
/// <see cref="DamageNumberLayer"/> and the <see cref="LockOnCueLayer"/>, so the three share one
/// canvas layer and one visibility rule.</para>
/// </summary>
public partial class CombatFeedbackOverlay : CanvasLayer
{
    private readonly FlashGate _gate = new();
    private ColorRect _flash = null!;
    private Label _word = null!;
    private Color _color = Colors.White;
    private float _peak;
    private float _hold = CombatFeedbackFx.HoldSeconds;
    private double _age;
    private bool _active;

    public override void _Ready()
    {
        _flash = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);

        AddChild(new DamageNumberLayer { Name = "DamageNumbers" });
        AddChild(new LockOnCueLayer { Name = "LockCues" });

        _word = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        _word.SetAnchorsPreset(Control.LayoutPreset.Center);
        _word.GrowHorizontal = Control.GrowDirection.Both;
        _word.GrowVertical = Control.GrowDirection.Both;
        UiTheme.ApplyType(_word, UiTheme.FontRole.Display, UiTheme.ShoutFontSize);
        _word.Position = new Vector2(0f, -120f);
        AddChild(_word);

        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHit);
    }

    public override void _ExitTree() => EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHit);

    private void OnHit(HitConfirmedEvent e)
    {
        CombatFeedback? state = CombatFeedbackFx.ForOutcome(e.Outcome, e.Kind, e.Staggered, e.ByPlayer, e.OnPlayer);
        if (state is { } s)
        {
            Flash(s);
        }
    }

    private void Flash(CombatFeedback state)
    {
        if (!_gate.Allow(Time.GetTicksMsec() / 1000.0, state))
        {
            return;
        }

        (float r, float g, float b) = CombatFeedbackFx.Tint(state);
        _color = new Color(r, g, b);

        // The flash is the part that can hurt: it follows the combat flash slider (Reduced Motion has
        // capped it) and the global motion switch. The word alone still communicates the state.
        _peak = UiTheme.MotionEnabled
            ? CombatFeedbackFx.FlashAlpha(state, LiveComfort.Get().ScreenFlash)
            : 0f;
        _hold = CombatFeedbackFx.Hold(state);
        _age = 0d;
        _active = true;
        _word.Text = Loc.T(CombatFeedbackFx.WordKey(state));
        _word.AddThemeColorOverride("font_color", _color);
    }

    public override void _Process(double delta)
    {
        Visible = GameManager.Instance is { IsPlaying: true };
        if (!_active)
        {
            return;
        }

        _age += delta;
        float t = (float)(_age / _hold);
        if (t >= 1f)
        {
            _active = false;
            _flash.Color = new Color(_color.R, _color.G, _color.B, 0f);
            _word.Modulate = new Color(1f, 1f, 1f, 0f);
            return;
        }

        float fade = 1f - t;
        _flash.Color = new Color(_color.R, _color.G, _color.B, _peak * fade);
        _word.Modulate = new Color(1f, 1f, 1f, fade);
    }
}
