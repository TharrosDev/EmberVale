using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Which way the hit came from (39.5B, §33): a short arc at the edge of the screen, in the
/// direction of whatever just damaged the player, fading over about a second.
///
/// ⚠️ <b>It answers the one question the rest of the combat HUD cannot.</b> The health bar says the
/// player was hit and <see cref="CombatFeedbackOverlay"/> says how, but in a third-person game with
/// a 90° strip compass, "who is hitting me and where are they" is otherwise only answerable by
/// spinning the camera — which is the worst possible thing to be doing while being hit from behind.
///
/// Deliberately restrained: no numbers, no screen-wide flash, no permanent element. It draws
/// nothing at all when nothing has hit the player recently, so it costs an empty <c>_Draw</c> during
/// exploration. Reduced motion keeps the arc (it is information, not decoration) and only drops the
/// fade to a hard on/off, matching how the rest of the HUD treats the setting.
/// </summary>
public sealed partial class DamageDirectionOverlay : Control
{
    /// <summary>How long one hit's arc stays on screen.</summary>
    private const float LifeSeconds = 1.1f;

    /// <summary>Half-width of the arc, in radians — wide enough to read as a direction at a glance,
    /// narrow enough that two attackers on different sides do not merge into one smear.</summary>
    private const float HalfArc = Mathf.Pi / 9f;

    /// <summary>Inset from the shorter screen edge, so the arc sits inside the safe area.</summary>
    private const float EdgeInset = 54f;

    /// <summary>Cap on simultaneous arcs. A pack of five all landing at once is a ring, not a
    /// direction; the oldest drops out so the newest hit is always the one that reads.</summary>
    private const int MaxMarks = 4;

    private readonly List<(float Bearing, float Age)> _marks = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
    }

    public override void _ExitTree() => EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);

    /// <summary>Clears every live arc — used when the HUD leaves gameplay, so a hit taken a moment
    /// before death or a menu cannot still be fading on screen afterwards (§52).</summary>
    public void Clear()
    {
        if (_marks.Count > 0)
        {
            _marks.Clear();
            QueueRedraw();
        }
    }

    private void OnDamage(DamageDealtEvent e)
    {
        // Only damage TO the player, and only from a source that still has a body to point at —
        // a fall, a burn tick or a dead attacker gives no direction, and an arrow pointing at the
        // world origin is worse than no arrow (§53).
        if (!IsPlayer(e.Target) || e.Source is not { Body: { } from } || !IsInstanceValid(from) ||
            ResolvePlayer() is not var (position, yaw))
        {
            return;
        }

        Vector3 offset = from.GlobalPosition - position;
        if (offset.LengthSquared() < 0.01f)
        {
            return;
        }

        // Relative to where the player is FACING, not to north: this marks a side of the screen, and
        // the screen turns with the camera. The compass strip is the north-referenced surface.
        float relative = CompassMath.Relative(CompassMath.BearingTo(offset.X, offset.Z), -yaw);

        _marks.Add((relative, 0f));
        if (_marks.Count > MaxMarks)
        {
            _marks.RemoveAt(0);
        }

        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        Visible = GameManager.Instance is { IsPlaying: true };
        if (_marks.Count == 0)
        {
            return;
        }

        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            float age = _marks[i].Age + (float)delta;
            if (age >= LifeSeconds)
            {
                _marks.RemoveAt(i);
            }
            else
            {
                _marks[i] = (_marks[i].Bearing, age);
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_marks.Count == 0)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        float radius = Mathf.Max(Mathf.Min(Size.X, Size.Y) * 0.5f - EdgeInset, 24f);
        Color tint = UiTheme.Adapt(UiTheme.AccentHot);

        foreach ((float bearing, float age) in _marks)
        {
            // Screen angles are measured from straight up, clockwise — the same frame the compass
            // uses, so "to my right" is the right of the screen in both.
            float alpha = UiTheme.MotionEnabled ? 1f - UiMotion.Progress(age, LifeSeconds) : 1f;
            float from = bearing - HalfArc - (Mathf.Pi * 0.5f);
            float to = bearing + HalfArc - (Mathf.Pi * 0.5f);

            DrawArc(centre, radius, from, to, 20, new Color(UiTheme.Engrave, alpha * 0.6f), 7f);
            DrawArc(centre, radius, from, to, 20, new Color(tint, alpha * 0.85f), 3.5f);
        }
    }

    private static bool IsPlayer(IEntity? entity) =>
        entity != null
        && ServiceLocator.Instance is { } locator
        && locator.TryGet(out PlayerCharacter player)
        && ReferenceEquals(entity, player);

    private static (Vector3 Position, float Yaw)? ResolvePlayer() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? (player.GlobalPosition, player.GlobalRotation.Y)
            : null;
}
