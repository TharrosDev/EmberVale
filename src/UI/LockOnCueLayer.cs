using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The cues that make a lock read: a thin ring that closes onto a small keylined dot
/// (<see cref="UiTheme.DrawLockDot"/>) on a newly locked target, the two fading out together, and — when a lock ends — a mark
/// that says <em>why</em>. A kill is a gold burst where the target fell; a target that
/// ran out of range or was lost behind cover is a red cross with a word ("Too far", "Lost sight"),
/// because a silently dropped lock is indistinguishable from a bug. Toggling the lock off yourself
/// draws nothing.
///
/// <para>Driven by <see cref="LockChangedEvent"/> and <see cref="LockBrokenEvent"/>, which
/// <see cref="LockOnComponent"/> publishes. Under Reduced Motion the marks hold still and simply fade
/// instead of contracting or bursting. A pure draw-layer: it owns no state a rule reads.</para>
/// </summary>
public sealed partial class LockOnCueLayer : Control
{
    private const float AcquireSeconds = 0.25f;
    private const float KillSeconds = 0.55f;
    private const float LostSeconds = 1.0f;

    /// <summary>Where the acquire ring starts, and how far outside the dot it comes to rest.</summary>
    private const float AcquireStartRadius = 44f;
    private const float AcquireRestGap = 5f;

    private enum Kind
    {
        Acquire,
        Kill,
        Lost,
    }

    private sealed class Cue
    {
        public Kind Kind;
        public float Age;
        public float Life;
        public Vector3 World;
        public IEntity? Follow;
        public string Word = "";
    }

    private readonly List<Cue> _cues = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        EventBus.Instance?.Subscribe<LockChangedEvent>(OnChanged);
        EventBus.Instance?.Subscribe<LockBrokenEvent>(OnBroken);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<LockChangedEvent>(OnChanged);
        EventBus.Instance?.Unsubscribe<LockBrokenEvent>(OnBroken);
    }

    /// <summary>How many cues are live — for the probe.</summary>
    public int LiveCount => _cues.Count;

    private void OnChanged(LockChangedEvent e)
    {
        if (e.Target == null || !CombatPerspective.IsPlayer(e.Player) ||
            e.Target.Body is not { } body || !IsInstanceValid(body))
        {
            return;
        }

        Add(new Cue
        {
            Kind = Kind.Acquire,
            Life = AcquireSeconds,
            World = BodyMetrics.AimPoint(body),
            Follow = e.Target,
        });
    }

    private void OnBroken(LockBrokenEvent e)
    {
        if (!CombatPerspective.IsPlayer(e.Player))
        {
            return;
        }

        switch (e.Reason)
        {
            case LockBreakReason.TargetDied:
                Add(new Cue { Kind = Kind.Kill, Life = KillSeconds, World = e.LastPoint });
                break;
            case LockBreakReason.OutOfRange:
                Add(new Cue { Kind = Kind.Lost, Life = LostSeconds, World = e.LastPoint, Word = Loc.T("combat.feedback.lock_far") });
                break;
            case LockBreakReason.LostSight:
                Add(new Cue { Kind = Kind.Lost, Life = LostSeconds, World = e.LastPoint, Word = Loc.T("combat.feedback.lock_sight") });
                break;
        }
    }

    private void Add(Cue cue)
    {
        if (_cues.Count >= 6)
        {
            _cues.RemoveAt(0);
        }

        _cues.Add(cue);
        QueueRedraw();
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
        if (_cues.Count == 0)
        {
            return;
        }

        for (int i = _cues.Count - 1; i >= 0; i--)
        {
            Cue cue = _cues[i];
            cue.Age += (float)delta;
            if (cue.Age >= cue.Life)
            {
                _cues.RemoveAt(i);
            }
            else if (cue.Follow is { Body: { } body } && IsInstanceValid(body))
            {
                cue.World = BodyMetrics.AimPoint(body);
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_cues.Count == 0 || GetViewport().GetCamera3D() is not { } camera)
        {
            return;
        }

        bool motion = UiTheme.MotionEnabled;
        foreach (Cue cue in _cues)
        {
            if (camera.IsPositionBehind(cue.World))
            {
                continue;
            }

            Vector2 at = camera.UnprojectPosition(cue.World);
            float t = cue.Age / cue.Life;
            float fade = 1f - t;
            switch (cue.Kind)
            {
                case Kind.Acquire:
                {
                    // Closes from wide onto the dot: "that one". Both fade as it lands, leaving the
                    // HUD's held reticle.
                    float rest = UiTheme.LockDotRadius + AcquireRestGap;
                    float radius = motion ? Mathf.Lerp(AcquireStartRadius, rest, UiMotion.EaseOut(t)) : rest;
                    DrawArc(at, radius, 0f, Mathf.Tau, 32, UiTheme.Keyline with { A = UiTheme.Keyline.A * fade }, 3f);
                    DrawArc(at, radius, 0f, Mathf.Tau, 32, new Color(UiTheme.AccentHot, fade), 1.5f);
                    UiTheme.DrawLockDot(this, at, fade);
                    break;
                }

                case Kind.Kill:
                {
                    // A gold burst where it fell: a ring going out and eight ticks flung with it.
                    float radius = motion ? Mathf.Lerp(12f, 58f, t) : 34f;
                    Color gold = new(UiTheme.Accent, fade);
                    DrawArc(at, radius, 0f, Mathf.Tau, 28, gold, 2.5f);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.Tau / 8f;
                        Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                        DrawLine(at + (dir * radius * 0.6f), at + (dir * radius * 0.95f), gold, 2f);
                    }

                    break;
                }

                default:
                {
                    // A red cross that shrinks away and says why the lock broke.
                    Color red = new(UiTheme.Bad, fade);
                    float size = motion ? Mathf.Lerp(20f, 12f, t) : 16f;
                    DrawLine(at + new Vector2(-size, -size), at + new Vector2(size, size), red, 3f);
                    DrawLine(at + new Vector2(-size, size), at + new Vector2(size, -size), red, 3f);
                    if (cue.Word.Length > 0)
                    {
                        Font font = (Font?)UiTheme.DisplayFont ?? ThemeDB.FallbackFont;
                        int fontSize = UiTheme.FontSize(UiTheme.HeaderFontSize);
                        Vector2 measure = font.GetStringSize(cue.Word, HorizontalAlignment.Left, -1f, fontSize);
                        Vector2 baseline = at + new Vector2(-measure.X * 0.5f, size + UiTheme.SpaceSm + fontSize);
                        DrawStringOutline(font, baseline, cue.Word, HorizontalAlignment.Left, -1f, fontSize,
                            UiTheme.HudInkSize * 2, UiTheme.Keyline with { A = UiTheme.Keyline.A * fade });
                        DrawString(font, baseline, cue.Word, HorizontalAlignment.Left, -1f, fontSize, red);
                    }

                    break;
                }
            }
        }
    }
}
