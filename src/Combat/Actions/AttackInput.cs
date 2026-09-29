namespace Embervale.Combat.Actions;

/// <summary>What the attack button means this frame.</summary>
public enum AttackIntent
{
    /// <summary>Nothing to do yet.</summary>
    None,

    /// <summary>A light swing (a tap, or a chain press while already acting).</summary>
    Light,

    /// <summary>The hold crossed the threshold: start charging.</summary>
    BeginCharge,

    /// <summary>The button came up during a charge: swing it.</summary>
    Release,
}

/// <summary>
/// The pure tap / hold / release decision behind the attack button. Godot-free; the input router
/// feeds it the raw button state and acts on the intent it returns.
///
/// <para><b>Chain presses are instant, opening presses wait a beat.</b> A press while the actor is
/// already mid-action (the combo, or the buffer) fires immediately: waiting there would make the
/// combo laggy. A press from rest cannot yet know whether it is a tap or the start of a hold, so it
/// waits until the button is released (a light swing) or held past
/// <see cref="ChargeRules.HoldThreshold"/> (a charge). The wait is the length of a tap.</para>
/// </summary>
public struct AttackInputState
{
    private enum Mode
    {
        Idle,
        Pending,
        Charging,

        /// <summary>A hold that could not start (no stamina): swallow the rest of it silently.</summary>
        Spent,
    }

    private Mode _mode;
    private float _held;

    /// <summary>True while a press from rest is still deciding between tap and hold.</summary>
    public readonly bool IsPending => _mode == Mode.Pending;

    /// <summary>True while a charge is being held.</summary>
    public readonly bool IsCharging => _mode == Mode.Charging;

    /// <summary>Forgets everything: a menu opened, control was lost.</summary>
    public void Reset()
    {
        _mode = Mode.Idle;
        _held = 0f;
    }

    /// <summary>The caller could not honour <see cref="AttackIntent.BeginCharge"/>: ignore the rest of
    /// this hold, and do not turn its release into a swing.</summary>
    public void Abort() => _mode = Mode.Spent;

    /// <summary>
    /// Advances one frame. <paramref name="busy"/> is whether the actor is already acting (a press then
    /// is a chain press and fires at once); <paramref name="holdThreshold"/> is how long a press waits
    /// before it becomes a charge.
    /// </summary>
    public AttackIntent Step(bool pressed, bool held, bool released, bool busy, float delta, float holdThreshold)
    {
        switch (_mode)
        {
            case Mode.Idle:
                if (!pressed)
                {
                    return AttackIntent.None;
                }

                if (busy)
                {
                    return AttackIntent.Light;
                }

                _mode = Mode.Pending;
                _held = 0f;

                // A press and release inside one frame is still a tap.
                return released ? Settle(AttackIntent.Light) : AttackIntent.None;

            case Mode.Pending:
                if (released || !held)
                {
                    return Settle(AttackIntent.Light);
                }

                _held += delta;
                if (_held >= holdThreshold)
                {
                    _mode = Mode.Charging;
                    return AttackIntent.BeginCharge;
                }

                return AttackIntent.None;

            case Mode.Charging:
                return released || !held ? Settle(AttackIntent.Release) : AttackIntent.None;

            default: // Spent
                if (released || !held)
                {
                    _mode = Mode.Idle;
                }

                return AttackIntent.None;
        }
    }

    private AttackIntent Settle(AttackIntent intent)
    {
        _mode = Mode.Idle;
        _held = 0f;
        return intent;
    }
}
