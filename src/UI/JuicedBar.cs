using Godot;

namespace Embervale.UI;

/// <summary>
/// A resource bar with value-change juice (30.5C): rises instantly, but drains with a short
/// lag so hits read as a visible chunk sliding off, and pulses the fill white-hot for a beat
/// when the value drops. Honours reduced motion (snaps, no pulse) via <see cref="UiTheme"/>.
/// Drive it with <see cref="SetTarget"/> each frame; use <see cref="Snap"/> when the subject
/// changes (new nameplate target, new boss) so the lag never animates across subjects.
///
/// Three things a HUD bar on the live world opts into, all off by default so the bars that predate
/// them are unchanged: <see cref="Keylined"/> (the dark outline and lighter inner edge that hold the
/// bar's shape over any scene), <see cref="LagChunk"/> (the fill shows the true value at once and a
/// pale chunk marks what a hit removed, the arithmetic in <see cref="JuicedBarRules"/>) and
/// <see cref="SetTicks"/> (threshold notches). <see cref="Hatched"/> rules the fill diagonally, for
/// a bar whose meaning must survive without its colour.
/// </summary>
public partial class JuicedBar : ProgressBar
{
    /// <summary>Normalized units drained per second while lagging down toward the target.</summary>
    private const float DrainPerSecond = 0.9f;
    private const float PulseSeconds = 0.25f;

    private StyleBoxFlat _fillBox = null!;
    private Color _fill;
    private double _target = 1d;
    private double _pulse;

    // Whether _Process is running. The bar sleeps once it has reached its target and the pulse has
    // decayed; SetTarget wakes it. Starts true so the first frame after entering the tree settles it.
    private bool _awake = true;

    private float[] _ticks = System.Array.Empty<float>();
    private double _lag = 1d;      // trailing edge of the damage chunk (LagChunk only)
    private double _lagHold;

    /// <summary>Spacing of the <see cref="Hatched"/> rules, in px.</summary>
    private const float HatchStep = 5f;

    /// <summary>Draws the HUD keyline round the bar (<see cref="UiTheme.DrawKeyline"/>).</summary>
    public bool Keylined { get; set; }

    /// <summary>The fill snaps to a lower value and a pale chunk shows what was lost, then closes.
    /// Without it the fill itself drains, which is how the bar has always behaved.</summary>
    public bool LagChunk { get; set; }

    /// <summary>Rules the fill with diagonal lines.</summary>
    public bool Hatched { get; set; }

    /// <summary>Marks fractions of the bar (0..1) with a notch: a threshold the player should be
    /// able to see coming.</summary>
    public void SetTicks(params float[] fractions)
    {
        _ticks = fractions;
        QueueRedraw();
    }

    /// <summary>Builds a themed bar (same look as <see cref="UiTheme.Bar"/>) with juice.</summary>
    public static JuicedBar Create(Color fill, float width = 168f)
    {
        var bar = new JuicedBar
        {
            MinValue = 0d,
            MaxValue = 1d,
            Value = 1d,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, 13f),
            _fill = fill,
            _fillBox = UiTheme.BarStyle(fill),
        };
        bar.AddThemeStyleboxOverride("background", UiTheme.BarStyle(UiTheme.Trough));
        bar.AddThemeStyleboxOverride("fill", bar._fillBox);
        return bar;
    }

    /// <summary>Sets the value the bar settles toward; a drop triggers the drain lag + pulse.</summary>
    public void SetTarget(double value)
    {
        value = Mathf.Clamp(value, 0d, 1d);
        if (!UiTheme.MotionEnabled)
        {
            // Asleep on this very value: the bar already shows it.
            if (value != _target || _awake)
            {
                _target = value;
                _lag = value;
                Value = value;
            }

            return;
        }

        if (value < _target - 0.001d)
        {
            _pulse = PulseSeconds;
            if (LagChunk)
            {
                _lag = JuicedBarRules.OnDrop(_lag, Value);
                _lagHold = JuicedBarRules.LagHoldSeconds;
            }
        }

        if (value == _target && !_awake)
        {
            return; // settled on this value already: nothing to animate
        }

        _target = value;
        Wake();
    }

    /// <summary>Jumps straight to <paramref name="value"/> with no lag or pulse (subject changed).</summary>
    public void Snap(double value)
    {
        _target = Mathf.Clamp(value, 0d, 1d);
        Value = _target;
        _lag = _target;
        _lagHold = 0d;
        _pulse = 0d;
        _fillBox.BgColor = _fill;
        QueueRedraw();
    }

    private void Wake()
    {
        if (!_awake)
        {
            _awake = true;
            SetProcess(true);
        }
    }

    public override void _Process(double delta)
    {
        // Rise instantly (heals feel responsive); drain with a lag (hits read as a sliding chunk).
        double shown = Value;
        bool chunk = false;
        if (LagChunk)
        {
            // The fill is the truth; only the chunk behind it takes its time.
            Value = _target;
            double lagBefore = _lag;
            (_lag, _lagHold) = JuicedBarRules.Step(_lag, _lagHold, _target, delta);
            chunk = !JuicedBarRules.Settled(_lag, _target);
            if (_lag != lagBefore)
            {
                QueueRedraw();
            }
        }
        else
        {
            Value = shown < _target ? _target : Mathf.MoveToward((float)shown, (float)_target, (float)delta * DrainPerSecond);
        }

        if (_pulse > 0d)
        {
            _pulse = Mathf.Max(_pulse - delta, 0d);
            _fillBox.BgColor = _fill.Lerp(Colors.White, (float)(_pulse / PulseSeconds) * 0.75f);
            return;
        }

        if (chunk)
        {
            return; // still closing
        }

        // Settled: this frame's write changed nothing and the bar sits on the target (to within the
        // range's own step snapping), so every further frame would write the same value. Stop ticking
        // until SetTarget moves the target again.
        double after = Value;
        if (after == shown && System.Math.Abs(after - _target) <= (Step * 0.5d) + 1e-9d)
        {
            _awake = false;
            SetProcess(false);
        }
    }

    /// <summary>The chunk, the hatch, the ticks and the keyline, over the fill the engine has already
    /// drawn. Nothing here runs for a bar that opted into none of them.</summary>
    public override void _Draw()
    {
        Vector2 size = Size;
        float fillEnd = (float)(Value * size.X);

        // Against the target, as _Process settles it, and not against Value: the range snaps Value
        // to its step, and the hundredth it can sit under the target would draw as a chunk at rest.
        if (LagChunk && !JuicedBarRules.Settled(_lag, _target))
        {
            float chunkEnd = (float)(_lag * size.X);
            if (chunkEnd > fillEnd)
            {
                DrawRect(new Rect2(fillEnd, 0f, chunkEnd - fillEnd, size.Y), UiTheme.HudChunk);
            }
        }

        if (Hatched && fillEnd > 1f)
        {
            // Rising diagonals, clipped by hand to the filled length so no clip rect is needed.
            for (float x = -size.Y; x < fillEnd; x += HatchStep)
            {
                float from = Mathf.Max(x, 0f);
                float to = Mathf.Min(x + size.Y, fillEnd);
                if (to > from)
                {
                    DrawLine(
                        new Vector2(from, size.Y - (from - x)), new Vector2(to, size.Y - (to - x)), UiTheme.Keyline, 1f);
                }
            }
        }

        foreach (float tick in _ticks)
        {
            float x = Mathf.Round(tick * size.X);
            DrawRect(new Rect2(x, 0f, 1f, size.Y), UiTheme.Keyline);
        }

        if (Keylined)
        {
            UiTheme.DrawKeyline(this, new Rect2(Vector2.Zero, size));
        }
    }
}
