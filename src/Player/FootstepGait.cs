namespace Embervale.Player;

/// <summary>
/// Pure stride accumulator (Phase 31E): sums horizontal distance travelled and fires a step every
/// <see cref="Stride"/> metres, so cadence scales naturally with speed (faster movement covers the
/// stride sooner). Godot-free so it unit-tests; the <see cref="FootstepComponent"/> feeds it real
/// per-frame distance and plays a cue whenever <see cref="Advance"/> returns true.
/// </summary>
public sealed class FootstepGait
{
    private float _accumulated;

    /// <summary>Metres between footfalls.</summary>
    public float Stride { get; set; } = 2.0f;

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

    /// <summary>Clears the accumulator (on stop/airborne) so movement resumes on a fresh stride.</summary>
    public void Reset() => _accumulated = 0f;
}
