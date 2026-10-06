using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// A screen-centre crosshair so the first-person aim point (where melee raycasts,
/// interaction and spells are directed) is visible. Drawn in code as four short arms
/// around a small centre dot, with a soft dark outline so it reads on any background.
/// It ignores mouse input so it never blocks menu clicks.
///
/// 30.5E adds a **hit-marker**: when the player lands damage the arms kick outward and
/// tint for a beat — ember orange on crits, bone pale otherwise — so hits confirm at the
/// point of aim without looking at the target's bar. Suppressed under reduced motion.
///
/// The reticle is positioned from the <em>viewport</em> centre rather than this
/// control's own rect: a <see cref="Control"/> parented to a <see cref="CanvasLayer"/>
/// can momentarily report a zero size, which would otherwise draw the cross in the
/// top-left corner instead of the middle of the screen.
/// </summary>
public partial class Crosshair : Control
{
    /// <summary>Gap between the centre and the start of each arm.</summary>
    [Export] public float Gap { get; set; } = 4f;

    /// <summary>Length of each arm.</summary>
    [Export] public float Length { get; set; } = 7f;

    [Export] public float Thickness { get; set; } = 2f;

    private const float HitPopSeconds = 0.18f;
    private const float HitPopDistance = 3.5f;

    // Bone-pale (the token text colour) so the reticle sits in the dying-world palette; the
    // HUD keyline under it keeps it readable on bright scenes.
    private static readonly Color LineColor = new(UiTheme.Text, 0.95f);
    private static readonly Color Outline = UiTheme.Keyline;

    private double _hitPop;     // seconds left of the hit-marker pop
    private Color _hitTint = LineColor;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Keep it centred when the window/viewport is resized.
        GetViewport().SizeChanged += QueueRedraw;

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);

        // Ticks only while a hit-marker pop is decaying (OnDamage wakes it).
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
    }

    private void OnDamage(DamageDealtEvent e)
    {
        if (!UiTheme.MotionEnabled ||
            ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            !ReferenceEquals(e.Source, player) ||
            ReferenceEquals(e.Target, player))
        {
            return;
        }

        _hitPop = HitPopSeconds;
        _hitTint = e.IsCrit ? new Color(UiTheme.AccentHot, 0.95f) : LineColor;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_hitPop <= 0d)
        {
            SetProcess(false);
            return;
        }

        _hitPop = Mathf.Max(_hitPop - delta, 0d);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = (GetViewportRect().Size * 0.5f).Round();

        // Arms kick outward and tint while the hit-marker pop decays.
        float pop = (float)(_hitPop / HitPopSeconds);
        float kick = pop * HitPopDistance;
        Color color = LineColor.Lerp(_hitTint, pop);

        DrawArm(c, Vector2.Right, kick, color);
        DrawArm(c, Vector2.Left, kick, color);
        DrawArm(c, Vector2.Up, kick, color);
        DrawArm(c, Vector2.Down, kick, color);

        // The centre dot, keylined like the arms: it is the aim point, and the one part of the
        // reticle that is never moved by a hit.
        DrawRect(new Rect2(c - new Vector2(2f, 2f), new Vector2(4f, 4f)), Outline);
        DrawRect(new Rect2(c - new Vector2(1f, 1f), new Vector2(2f, 2f)), color);
    }

    private void DrawArm(Vector2 centre, Vector2 direction, float kick, Color color)
    {
        Vector2 from = centre + (direction * (Gap + kick));
        Vector2 to = centre + (direction * (Gap + kick + Length));

        // The keyline first, a pixel proud of the arm on every side, gives the bright reticle
        // contrast on light scenes without the lopsided look of an offset shadow.
        DrawLine(from - direction, to + direction, Outline, Thickness + 2f);
        DrawLine(from, to, color, Thickness);
    }
}
