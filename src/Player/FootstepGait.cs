namespace Embervale.Player;

/// <summary>Which foot just struck the ground, if either.</summary>
public enum Footfall
{
    None,
    Left,
    Right,
}

/// <summary>
/// Decides when a footfall happens. Godot-free so it unit-tests; the <see cref="FootstepComponent"/>
/// feeds it real per-frame distance and, when the rig has feet, the two animated foot heights.
///
/// <para><b>Animation first (the 2026-09 upgrade).</b> <see cref="Step"/> fires when the LOWER foot
/// changes — the moment the swinging foot comes down past the one it was standing on is the moment it
/// takes the weight, which is the contact a player hears. That keeps the sound on the boot whatever
/// the blend space is doing, and alternates left and right by construction. <see cref="Margin"/> is
/// the hysteresis that stops two feet level at a standstill chattering.</para>
///
/// <para><b>Distance as the fallback.</b> A rig with no feet (a wolf, a capsule placeholder) or a
/// clip that slides rather than steps still gets a footfall every <see cref="FallbackDistance"/>
/// metres, alternating feet. That is the original Phase 31E stride accumulator, and
/// <see cref="Advance"/> is still exactly it.</para>
/// </summary>
public sealed class FootstepGait
{
    private float _accumulated;
    private float _travelled;
    private Footfall _lower;
    private Footfall _last;

    /// <summary>Metres between footfalls for <see cref="Advance"/> (cadence = speed / stride).</summary>
    public float Stride { get; set; } = 2.0f;

    /// <summary>How much lower one foot must be than the other to count as the standing foot.</summary>
    public float Margin { get; set; } = 0.03f;

    /// <summary>Metres without an animated contact after which <see cref="Step"/> fires anyway.</summary>
    public float FallbackDistance { get; set; } = 2.5f;

    /// <summary>Least distance between two footfalls, so a stumble in the blend cannot double-fire.</summary>
    public float MinSpacing { get; set; } = 0.25f;

    /// <summary>The foot that last struck the ground.</summary>
    public Footfall Last => _last;

    /// <summary>Adds this frame's horizontal distance; true when a footfall is due (consumes one stride).</summary>
    public bool Advance(float distance)
    {
        if (distance <= 0f || Stride <= 0f)
        {
            return false;
        }

        _accumulated += distance;
        if (_accumulated >= Stride)
        {
            _accumulated -= Stride;
            return true;
        }

        return false;
    }

    /// <summary>
    /// One frame of movement: <paramref name="distance"/> travelled, and each foot's animated height
    /// (null when the rig has no such foot). Returns the foot that just struck, or
    /// <see cref="Footfall.None"/>.
    /// </summary>
    public Footfall Step(float distance, float? leftHeight, float? rightHeight)
    {
        if (distance > 0f)
        {
            _travelled += distance;
        }

        if (leftHeight is { } left && rightHeight is { } right)
        {
            Footfall lower = left < right - Margin ? Footfall.Left
                : right < left - Margin ? Footfall.Right
                : Footfall.None;
            if (lower != Footfall.None && lower != _lower)
            {
                // The first reading after a stop is the foot already standing, not a new contact.
                bool first = _lower == Footfall.None;
                _lower = lower;
                if (!first && _travelled >= MinSpacing)
                {
                    return Fire(lower);
                }
            }
        }

        if (FallbackDistance > 0f && _travelled >= FallbackDistance)
        {
            return Fire(_last == Footfall.Left ? Footfall.Right : Footfall.Left);
        }

        return Footfall.None;
    }

    /// <summary>Clears the accumulators (on stop/airborne) so movement resumes on a fresh stride.
    /// The last foot is kept, so the next fallback step still alternates.</summary>
    public void Reset()
    {
        _accumulated = 0f;
        _travelled = 0f;
        _lower = Footfall.None;
    }

    private Footfall Fire(Footfall foot)
    {
        _last = foot;
        _travelled = 0f;
        return foot;
    }
}
