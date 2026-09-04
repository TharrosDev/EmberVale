namespace Embervale.Enemies;

/// <summary>
/// The two clocks an AI actor runs on, and the reason they are different.
///
/// <para>A live enemy far from the player thinks rarely — it wakes once per
/// <see cref="AIProfileResource.SleepInterval"/> instead of every physics frame. That is the level
/// of detail that keeps a populated region affordable, and it introduced a bug that took a while to
/// see: <b>the wall-clock timers advanced by one frame per sleep interval instead of by the
/// interval</b>, so a distant enemy's twelve-second provoke memory ran for six real minutes and it
/// never stood down.</para>
///
/// <para>So skipped time is <em>banked</em> and handed back on the tick that wakes. State duration,
/// provoke memory and the retreat cooldown read that wall clock; movement and turn slew keep the raw
/// frame delta, because stepping a sleeping actor by half a second of motion would teleport it.</para>
/// </summary>
public struct AiLodClock
{
    private double _sleepTimer;
    private double _bankedSeconds;

    /// <summary>
    /// Advances the clock for a frame in which the actor is out of range.
    /// </summary>
    /// <returns>True when the actor should skip this frame entirely.</returns>
    public bool ShouldSleep(double delta, double sleepInterval)
    {
        _sleepTimer -= delta;
        if (_sleepTimer > 0d)
        {
            _bankedSeconds += delta;
            return true;
        }

        _sleepTimer = sleepInterval;
        return false;
    }

    /// <summary>
    /// Real time since this brain last thought: the frame's own delta plus whatever it slept
    /// through. Consumes the bank, so calling it twice in one tick would under-count — it is called
    /// once, at the top.
    /// </summary>
    public double ConsumeWallSeconds(double delta)
    {
        double wall = delta + _bankedSeconds;
        _bankedSeconds = 0d;
        return wall;
    }

    /// <summary>Seconds currently banked. Exposed for the tests that pin the bug above.</summary>
    public readonly double Banked => _bankedSeconds;
}
