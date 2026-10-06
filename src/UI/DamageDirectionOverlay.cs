using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Which way the hit came from (39.5B, §33), and now which way the next one is coming from: a short
/// arc at the edge of the screen, in the direction of whatever just damaged the player, fading over
/// about a second — and a pulsing warning arc for a hostile that is winding up a blow from somewhere
/// the player cannot see.
///
/// <para>⚠️ <b>It answers the one question the rest of the combat HUD cannot.</b> The health bar says the
/// player was hit and <see cref="CombatFeedbackOverlay"/> says how, but in a third-person game with
/// a 90° strip compass, "who is hitting me and where are they" is otherwise only answerable by
/// spinning the camera — which is the worst possible thing to be doing while being hit from behind.</para>
///
/// <para>Hit arcs read <see cref="HitConfirmedEvent"/>: thicker for a blow that took a large share of
/// the player's health, steel for one the guard took, hot red for a crit or a broken guard. A parried
/// blow draws none — nothing landed. Warning arcs come from <see cref="AttackPerformedEvent"/> and only
/// for attackers that are behind or beside the player (past the edge of the view) and close; their
/// shape carries the <see cref="TelegraphClass"/> (a plain arc, a gold arc for a parryable strike, a
/// double red arc for one to dodge, a wide arc for a sweep) and they end with the wind-up or the
/// moment it is interrupted.</para>
///
/// Deliberately restrained: no numbers, no screen-wide flash, no permanent element. It draws
/// nothing at all when nothing has hit the player recently, so it costs an empty <c>_Draw</c> during
/// exploration. Reduced motion keeps the arc (it is information, not decoration) and only drops the
/// fade and the pulse to a hard on/off, matching how the rest of the HUD treats the setting.
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

    /// <summary>A wind-up is only warned about from this close, metres.</summary>
    private const float WarnRange = 9f;

    /// <summary>Past this angle from straight ahead (radians, about 100 degrees) an attacker is off
    /// the edge of the view and worth a warning arc.</summary>
    private const float OffViewAngle = 1.75f;

    private readonly List<Mark> _marks = new();
    private readonly Dictionary<ulong, Warning> _warnings = new();

    private struct Mark
    {
        public float Bearing;
        public float Age;
        public HitOutcome Outcome;
        public float Weight;
    }

    private sealed class Warning
    {
        public IEntity Attacker = null!;
        public TelegraphClass Class;
        public float Age;
        public float Life;
        public float Bearing;
        public bool Visible;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHit);
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHit);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    /// <summary>Clears every live arc — used when the HUD leaves gameplay, so a hit taken a moment
    /// before death or a menu cannot still be fading on screen afterwards (§52).</summary>
    public void Clear()
    {
        if (_marks.Count > 0 || _warnings.Count > 0)
        {
            _marks.Clear();
            _warnings.Clear();
            QueueRedraw();
        }
    }

    /// <summary>How many warning arcs are tracked — for the probe.</summary>
    public int WarningCount => _warnings.Count;

    private void OnHit(HitConfirmedEvent e)
    {
        // Only damage TO the player, and only from a source that still has a body to point at —
        // a fall, a burn tick or a dead attacker gives no direction, and an arrow pointing at the
        // world origin is worse than no arrow (§53). A parry means nothing landed.
        if (!e.OnPlayer || e.Outcome == HitOutcome.Parried ||
            e.Source is not { Body: { } from } || !IsInstanceValid(from) ||
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

        float maxHealth = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StatsComponent>()?.GetMax(StatType.Health) ?? 0f
            : 0f;
        float weight = maxHealth > 0f ? Mathf.Clamp(e.Amount / (maxHealth * 0.25f), 0f, 1f) : 0.3f;

        _marks.Add(new Mark { Bearing = relative, Outcome = e.Outcome, Weight = weight });
        if (_marks.Count > MaxMarks)
        {
            _marks.RemoveAt(0);
        }

        QueueRedraw();
    }

    private void OnAttack(AttackPerformedEvent e)
    {
        if (CombatPerspective.IsPlayer(e.Attacker) || e.WindupSeconds <= 0f ||
            e.Attacker.GetComponent<CombatComponent>() is not { Team: not 0 } ||
            e.Attacker.Body is not { } body || !IsInstanceValid(body) ||
            ResolvePlayer() is not var (position, _) ||
            body.GlobalPosition.DistanceTo(position) > WarnRange)
        {
            return;
        }

        _warnings[e.Attacker.RuntimeId] = new Warning
        {
            Attacker = e.Attacker,
            Class = TelegraphClasses.Of(e.Attacker) ?? TelegraphClass.Standard,
            Life = e.WindupSeconds + 0.12f,
        };
    }

    private void OnInterrupted(AttackInterruptedEvent e)
    {
        if (_warnings.Remove(e.Attacker.RuntimeId))
        {
            QueueRedraw();
        }
    }

    // Whether the layer is currently shown for play (-1 = not yet decided), so its visibility is
    // written when the game state changes instead of restated to the engine every frame.
    private int _shownForPlay = -1;

    private void ShowForPlay()
    {
        int playing = GameManager.Instance is { IsPlaying: true } ? 1 : 0;
        if (playing != _shownForPlay)
        {
            _shownForPlay = playing;
            Visible = playing == 1;
        }
    }

    public override void _Process(double delta)
    {
        ShowForPlay();
        if (_marks.Count == 0 && _warnings.Count == 0)
        {
            return;
        }

        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            Mark mark = _marks[i];
            mark.Age += (float)delta;
            if (mark.Age >= LifeSeconds)
            {
                _marks.RemoveAt(i);
            }
            else
            {
                _marks[i] = mark;
            }
        }

        if (_warnings.Count > 0)
        {
            TickWarnings((float)delta);
        }

        QueueRedraw();
    }

    private void TickWarnings(float dt)
    {
        List<ulong>? done = null;
        (Vector3 Position, float Yaw)? player = ResolvePlayer();
        foreach ((ulong id, Warning warning) in _warnings)
        {
            warning.Age += dt;
            if (warning.Age >= warning.Life || player is not { } p ||
                warning.Attacker.Body is not { } body || !IsInstanceValid(body))
            {
                (done ??= new List<ulong>()).Add(id);
                continue;
            }

            Vector3 offset = body.GlobalPosition - p.Position;
            warning.Bearing = CompassMath.Relative(CompassMath.BearingTo(offset.X, offset.Z), -p.Yaw);
            warning.Visible = Mathf.Abs(Mathf.Wrap(warning.Bearing, -Mathf.Pi, Mathf.Pi)) > OffViewAngle;
        }

        if (done != null)
        {
            foreach (ulong id in done)
            {
                _warnings.Remove(id);
            }
        }
    }

    public override void _Draw()
    {
        if (_marks.Count == 0 && _warnings.Count == 0)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        float radius = Mathf.Max(Mathf.Min(Size.X, Size.Y) * 0.5f - EdgeInset, 24f);
        Color tint = UiTheme.Adapt(UiTheme.AccentHot);

        foreach (Mark mark in _marks)
        {
            // Screen angles are measured from straight up, clockwise — the same frame the compass
            // uses, so "to my right" is the right of the screen in both.
            float alpha = UiTheme.MotionEnabled ? 1f - UiMotion.Progress(mark.Age, LifeSeconds) : 1f;
            float half = HalfArc * (1f + (0.5f * mark.Weight));
            float from = mark.Bearing - half - (Mathf.Pi * 0.5f);
            float to = mark.Bearing + half - (Mathf.Pi * 0.5f);
            Color colour = mark.Outcome switch
            {
                HitOutcome.Blocked => UiTheme.Adapt(UiTheme.Poise),
                HitOutcome.Resisted => UiTheme.Adapt(new Color(0.55f, 0.57f, 0.66f)),
                HitOutcome.Critical or HitOutcome.GuardBroken => UiTheme.Bad,
                _ => tint,
            };
            float width = 3.5f + (4f * mark.Weight);

            DrawArc(centre, radius, from, to, 20, new Color(UiTheme.Keyline, alpha * 0.6f), width + 3.5f);
            DrawArc(centre, radius, from, to, 20, new Color(colour, alpha * 0.85f), width);
        }

        float pulseClock = Time.GetTicksMsec() / 1000f;
        foreach (Warning warning in _warnings.Values)
        {
            if (!warning.Visible)
            {
                continue;
            }

            float progress = Mathf.Clamp(warning.Age / warning.Life, 0f, 1f);
            float pulse = UiTheme.MotionEnabled ? 0.65f + (0.35f * Mathf.Sin(pulseClock * (12f + (10f * progress)))) : 1f;
            float alpha = (0.45f + (0.5f * progress)) * pulse;
            float half = warning.Class == TelegraphClass.Sweep ? HalfArc * 2.4f : HalfArc * 1.1f;
            float from = warning.Bearing - half - (Mathf.Pi * 0.5f);
            float to = warning.Bearing + half - (Mathf.Pi * 0.5f);
            float warnRadius = radius - 18f;
            Color colour = warning.Class switch
            {
                TelegraphClass.Parryable => UiTheme.Adapt(new Color(1.0f, 0.82f, 0.35f)),
                TelegraphClass.Unblockable => UiTheme.Adapt(new Color(1.0f, 0.2f, 0.1f)),
                TelegraphClass.Sweep => UiTheme.Adapt(new Color(0.95f, 0.6f, 0.25f)),
                _ => UiTheme.Adapt(new Color(0.85f, 0.88f, 0.92f)),
            };

            DrawArc(centre, warnRadius, from, to, 16, new Color(UiTheme.Keyline, alpha * 0.6f), 9f);
            DrawArc(centre, warnRadius, from, to, 16, new Color(colour, alpha), 5f);
            if (warning.Class == TelegraphClass.Unblockable)
            {
                DrawArc(centre, warnRadius - 12f, from, to, 16, new Color(colour, alpha), 3f);
            }
        }
    }

    private static (Vector3 Position, float Yaw)? ResolvePlayer() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? (player.GlobalPosition, player.GlobalRotation.Y)
            : null;
}
