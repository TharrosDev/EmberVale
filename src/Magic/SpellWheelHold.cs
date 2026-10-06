namespace Embervale.Magic;

/// <summary>What the spell-wheel button means this frame.</summary>
public enum SpellWheelIntent
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>The press became a hold (or a toggle press): open the wheel.</summary>
    Open,

    /// <summary>Close the wheel and select what the cursor is over.</summary>
    Confirm,

    /// <summary>Close the wheel without selecting.</summary>
    Cancel,

    /// <summary>A tap: swap back to the previous spell.</summary>
    Previous,

    /// <summary>The wheel could not open: step to the next spell instead.</summary>
    Fallback,
}

/// <summary>
/// The pure press / hold / release decision behind the spell-wheel button, in the shape of
/// <c>AttackInputState</c>. Godot-free; the input router feeds it the raw button state and acts on
/// the intent it returns.
///
/// <para><b>Held:</b> a press waits. Released inside <see cref="SpellWheelRules.TapSeconds"/> it is
/// <see cref="SpellWheelIntent.Previous"/>; held past it the wheel opens, and letting go confirms.</para>
///
/// <para><b>Toggled</b> (presses in place of holds): the press opens the wheel at once and it stays
/// open. A second press, or the confirm input, selects. There is no tap in this mode.</para>
///
/// <para>The cancel input closes without selecting in both. When the caller cannot open the wheel it
/// says so with <see cref="Refuse"/>, and the press becomes <see cref="SpellWheelIntent.Fallback"/>.</para>
/// </summary>
public struct SpellWheelHold
{
    private enum Mode
    {
        Idle,
        Pending,
        Open,

        /// <summary>A hold whose wheel could not open: its release is the fallback.</summary>
        Refused,

        /// <summary>A hold that has already been answered: swallow the rest of it.</summary>
        Spent,
    }

    private Mode _mode;
    private float _held;
    private bool _toggled;

    /// <summary>True while a press is still deciding between tap and hold.</summary>
    public readonly bool IsPending => _mode == Mode.Pending;

    /// <summary>True from <see cref="SpellWheelIntent.Open"/> until the wheel is closed.</summary>
    public readonly bool IsOpen => _mode == Mode.Open;

    /// <summary>True when the open wheel was opened by a toggle press rather than a hold.</summary>
    public readonly bool IsToggled => _mode == Mode.Open && _toggled;

    /// <summary>Forgets everything: a menu opened, control was lost. The caller closes the wheel.</summary>
    public void Reset()
    {
        _mode = Mode.Idle;
        _held = 0f;
        _toggled = false;
    }

    /// <summary>
    /// The caller could not honour <see cref="SpellWheelIntent.Open"/>. A toggle press falls back at
    /// once (the return value); a hold falls back when it is released.
    /// </summary>
    public SpellWheelIntent Refuse()
    {
        if (_mode != Mode.Open)
        {
            return SpellWheelIntent.None;
        }

        if (_toggled)
        {
            Reset();
            return SpellWheelIntent.Fallback;
        }

        _mode = Mode.Refused;
        return SpellWheelIntent.None;
    }

    /// <summary>The wheel closed by itself (a stagger, a menu). <paramref name="held"/> is whether the
    /// button is still down, so the rest of that hold does nothing.</summary>
    public void Closed(bool held)
    {
        if (_mode == Mode.Open)
        {
            _mode = held && !_toggled ? Mode.Spent : Mode.Idle;
            _toggled = false;
        }
    }

    /// <summary>
    /// Advances one frame. <paramref name="cursorTravel"/> is how far the wheel's cursor has moved
    /// since the press, in wheel units. <paramref name="toggle"/> is the presses-in-place-of-holds
    /// setting; <paramref name="confirm"/> and <paramref name="cancel"/> are this frame's presses of
    /// the inputs that select and dismiss an open wheel.
    /// </summary>
    public SpellWheelIntent Step(
        bool pressed, bool held, bool released, float delta, float cursorTravel, bool toggle, bool confirm, bool cancel)
    {
        switch (_mode)
        {
            case Mode.Idle:
                if (!pressed)
                {
                    return SpellWheelIntent.None;
                }

                _held = 0f;
                if (toggle)
                {
                    _mode = Mode.Open;
                    _toggled = true;
                    return SpellWheelIntent.Open;
                }

                _toggled = false;
                _mode = Mode.Pending;

                // A press and release inside one frame is still a tap.
                return released ? Settle(SpellWheelIntent.Previous) : SpellWheelIntent.None;

            case Mode.Pending:
                if (released || !held)
                {
                    return Settle(SpellWheelIntent.Previous);
                }

                _held += delta;
                if (SpellWheelRules.Classify(_held, cursorTravel) == SpellWheelGesture.Wheel)
                {
                    _mode = Mode.Open;
                    return SpellWheelIntent.Open;
                }

                return SpellWheelIntent.None;

            case Mode.Open:
                if (cancel)
                {
                    // A cancelled hold is still held: its release must not select or step.
                    bool stillHeld = !_toggled && held && !released;
                    _mode = stillHeld ? Mode.Spent : Mode.Idle;
                    _toggled = false;
                    return SpellWheelIntent.Cancel;
                }

                if (_toggled ? pressed || confirm : released || !held)
                {
                    return Settle(SpellWheelIntent.Confirm);
                }

                return SpellWheelIntent.None;

            case Mode.Refused:
                return released || !held ? Settle(SpellWheelIntent.Fallback) : SpellWheelIntent.None;

            default: // Spent
                if (released || !held)
                {
                    Reset();
                }

                return SpellWheelIntent.None;
        }
    }

    private SpellWheelIntent Settle(SpellWheelIntent intent)
    {
        Reset();
        return intent;
    }
}
