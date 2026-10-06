using System;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The hold-to-confirm gauge: a ring that fills while its input is held and calls
/// <see cref="Completed"/> when it closes. For irreversible actions only (deleting or overwriting
/// a save, resetting settings or bindings, a respec); a hold on anything else is friction.
/// Build one with <see cref="UiFx.HoldRing"/>.
///
/// Three ways to drive it, and they mix: <see cref="Attach"/> to a button (mouse, and
/// <c>ui_accept</c> on the focused button), set <see cref="HoldAction"/> to follow an input action
/// while the ring is visible, or call <see cref="Press"/> and <see cref="Release"/> directly.
///
/// With the <c>HoldsToPresses</c> setting on (<see cref="UiFx.HoldsToPresses"/>) a press completes
/// it at once and the ring stays an empty outline. The hold time itself is never shortened by
/// reduced motion: it is the safeguard, not an animation. Only the drain after letting go is.
///
/// The hold is timed on the wall clock, so it runs the same while the tree is paused and during
/// hit-stop. It processes only while filling or draining and wakes itself on <see cref="Press"/>
/// (NOW.md invariant 45); being hidden or leaving the tree cancels a hold in progress.
/// </summary>
public partial class HoldRing : Control
{
    /// <summary>Seconds a released ring takes to empty from full.</summary>
    private const float DrainSeconds = 0.18f;

    private float _progress;
    private bool _held;
    private bool _fired;
    private ulong _lastUsec;
    private StringName? _holdAction;

    /// <summary>Seconds the input must be held. Zero or less completes on press.</summary>
    public float Seconds { get; set; } = UiFx.HoldSeconds;

    /// <summary>Called once per completed hold (or per press, in press mode).</summary>
    public Action? Completed { get; set; }

    /// <summary>Stroke width of the ring, in px.</summary>
    public float Thickness { get; set; } = 3f;

    /// <summary>An input action that drives the ring while it is visible, or null for none. The
    /// event is not consumed, so whatever else listens for the action still hears it.</summary>
    public StringName? HoldAction
    {
        get => _holdAction;
        set
        {
            _holdAction = value;
            SetProcessInput(value != null);
        }
    }

    /// <summary>How full the ring is, 0..1.</summary>
    public float Progress => _progress;

    /// <summary>Whether the input is down right now.</summary>
    public bool Holding => _held;

    public HoldRing()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        // Overriding _Process and _Input switches both on; neither is wanted until something is held.
        SetProcess(_held || _progress > 0f);
        SetProcessInput(_holdAction != null);
    }

    /// <summary>Holds the ring down with <paramref name="button"/>: its press starts the hold and
    /// its release lets go. Leave the button's own <c>Pressed</c> unwired, or a tap confirms.</summary>
    public void Attach(BaseButton button)
    {
        button.ButtonDown += Press;
        button.ButtonUp += Release;
    }

    /// <summary>The input went down.</summary>
    public void Press()
    {
        if (_held)
        {
            return;
        }

        if (UiFx.HoldsToPresses || Seconds <= 0f)
        {
            Completed?.Invoke();
            return;
        }

        _held = true;
        _fired = false;
        _lastUsec = Time.GetTicksUsec();
        SetProcess(true);
    }

    /// <summary>The input came up. The ring drains and puts itself back to sleep.</summary>
    public void Release() => _held = false;

    public override void _Input(InputEvent @event)
    {
        if (_holdAction is not { } action || !IsVisibleInTree())
        {
            return;
        }

        if (@event.IsActionPressed(action))
        {
            Press();
        }
        else if (@event.IsActionReleased(action))
        {
            Release();
        }
    }

    public override void _Notification(int what)
    {
        // A ring that is hidden or removed mid-hold must not complete behind the player's back,
        // nor come back half full.
        if ((what == NotificationVisibilityChanged && !IsVisibleInTree()) || what == NotificationExitTree)
        {
            Cancel();
        }
    }

    private void Cancel()
    {
        if (!_held && _progress <= 0f)
        {
            return;
        }

        _held = false;
        _fired = false;
        _progress = 0f;
        SetProcess(false);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        float elapsed = (now - _lastUsec) / 1_000_000f;
        _lastUsec = now;

        float before = _progress;
        _progress = UiMotion.HoldStep(_progress, _held, elapsed, Seconds, UiTheme.Duration(DrainSeconds));
        if (_progress != before)
        {
            QueueRedraw();
        }

        if (!_held && _progress <= 0f)
        {
            SetProcess(false);
            return;
        }

        // Last, because the callback may rebuild the screen this ring is on.
        if (_held && _progress >= 1f && !_fired)
        {
            _fired = true;
            Completed?.Invoke();
        }
    }

    public override void _Draw()
    {
        float width = UiTheme.HighContrast ? Thickness + 1f : Thickness;
        Vector2 centre = Size * 0.5f;
        float radius = (Mathf.Min(Size.X, Size.Y) * 0.5f) - (width * 0.5f);
        if (radius <= 0f)
        {
            return;
        }

        DrawArc(centre, radius, 0f, Mathf.Tau, 32, UiTheme.Rule, width, true);
        if (_progress <= 0f)
        {
            return;
        }

        // From twelve o'clock, clockwise, heating as it closes.
        const float Start = -Mathf.Pi * 0.5f;
        int points = Mathf.Max(3, Mathf.CeilToInt(32f * _progress) + 1);
        DrawArc(centre, radius, Start, Start + (Mathf.Tau * _progress), points,
            UiTheme.Accent.Lerp(UiTheme.AccentHot, _progress), width, true);
    }
}
