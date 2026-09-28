namespace Embervale.Movement;

/// <summary>
/// Coyote time and the jump buffer — pure, Godot-free, unit-tested. <see cref="LocomotionComponent"/>
/// owns the state; this owns the rule.
///
/// Two forgiveness windows, both a tenth of a second or so, both invisible when they work:
/// <list type="bullet">
/// <item><b>Coyote time</b> — a jump pressed just <em>after</em> walking off an edge still fires. Without
/// it the one frame the capsule's centre crosses the lip eats the press, and the player reads it as the
/// game dropping their input at exactly the moment it mattered.</item>
/// <item><b>The buffer</b> — a jump pressed just <em>before</em> landing fires on the landing frame, and
/// one pressed during a roll fires as the roll ends. Otherwise a press a few frames early is lost.</item>
/// </list>
///
/// ⚠️ <b>A FIRED JUMP SPENDS BOTH WINDOWS.</b> The body is still inside its coyote window on the frame it
/// leaves the ground, so a jump that did not consume it could be pressed again in mid-air: a double
/// jump nobody designed.
/// </summary>
public static class JumpAssist
{
    /// <summary>Seconds since the body was last grounded and since jump was last pressed.
    /// <see cref="float.PositiveInfinity"/> means "not recently".</summary>
    public readonly record struct State(float SinceGrounded, float SincePressed);

    /// <summary>A body that has neither stood nor asked to jump recently.</summary>
    public static State Fresh => new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    /// Advances both clocks one frame and answers whether the body jumps now.
    /// <paramref name="canJump"/> is false while something else owns the body (a roll, flight); a
    /// press made then stays buffered rather than being dropped.
    /// </summary>
    public static (State Next, bool Jump) Step(
        State state, bool grounded, bool pressed, bool canJump, float delta, float coyote, float buffer)
    {
        if (!float.IsFinite(delta) || delta < 0f)
        {
            delta = 0f;
        }

        float sinceGrounded = grounded ? 0f : state.SinceGrounded + delta;
        float sincePressed = pressed ? 0f : state.SincePressed + delta;

        bool jump = canJump && sincePressed <= buffer && sinceGrounded <= coyote;
        return jump
            ? (Fresh, true)
            : (new State(sinceGrounded, sincePressed), false);
    }
}
