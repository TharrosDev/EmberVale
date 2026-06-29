using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Screen feedback for the player's combat states (Phase 29D): a brief full-screen colour flash plus a
/// short word — distinct per state (crit gold, block steel, stagger red, parry bright) — so a hit reads at
/// a glance. Reacts to the combat events from the player's perspective (resolved via the
/// <see cref="ServiceLocator"/>); colours/intensity come from <see cref="CombatFeedbackFx"/>.
/// </summary>
public partial class CombatFeedbackOverlay : CanvasLayer
{
    private ColorRect _flash = null!;
    private Label _word = null!;
    private Color _color = Colors.White;
    private float _peak;
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
        _word.AddThemeFontSizeOverride("font_size", 40);
        _word.Position = new Vector2(0f, -120f);
        AddChild(_word);

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Subscribe<EntityParriedEvent>(OnParried);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Unsubscribe<EntityParriedEvent>(OnParried);
    }

    private static bool IsPlayer(IEntity? entity) =>
        entity != null
        && ServiceLocator.Instance is { } sl
        && sl.TryGet(out PlayerCharacter player)
        && ReferenceEquals(entity, player);

    private void OnDamage(DamageDealtEvent e)
    {
        if (e.IsCrit && IsPlayer(e.Source))
        {
            Flash(CombatFeedback.Crit, "feedback.crit");
        }
        else if (e.IsBlocked && IsPlayer(e.Target))
        {
            Flash(CombatFeedback.Block, "feedback.block");
        }
    }

    private void OnStaggered(EntityStaggeredEvent e)
    {
        if (IsPlayer(e.Entity))
        {
            Flash(CombatFeedback.Stagger, "feedback.stagger");
        }
    }

    private void OnParried(EntityParriedEvent e)
    {
        if (IsPlayer(e.Defender))
        {
            Flash(CombatFeedback.Parry, "feedback.parry");
        }
    }

    private void Flash(CombatFeedback state, string wordKey)
    {
        (float r, float g, float b) = CombatFeedbackFx.Tint(state);
        _color = new Color(r, g, b);
        _peak = CombatFeedbackFx.PeakAlpha(state);
        _age = 0d;
        _active = true;
        _word.Text = Loc.T(wordKey);
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
        float t = (float)(_age / CombatFeedbackFx.HoldSeconds);
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
