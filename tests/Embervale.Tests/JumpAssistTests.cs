using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>Coyote time and the jump buffer (<see cref="JumpAssist"/>).</summary>
public class JumpAssistTests
{
    private const float Frame = 1f / 60f;
    private const float Window = 0.12f;

    private static (JumpAssist.State, bool) Step(JumpAssist.State s, bool grounded, bool pressed, bool canJump = true) =>
        JumpAssist.Step(s, grounded, pressed, canJump, Frame, Window, Window);

    [Fact]
    public void APressOnTheGroundJumps()
    {
        (_, bool jump) = Step(JumpAssist.Fresh, grounded: true, pressed: true);
        Assert.True(jump);
    }

    [Fact]
    public void NoPressNoJump()
    {
        (_, bool jump) = Step(JumpAssist.Fresh, grounded: true, pressed: false);
        Assert.False(jump);
    }

    [Fact]
    public void APressJustAfterLeavingTheEdgeStillJumps()
    {
        (JumpAssist.State s, _) = Step(JumpAssist.Fresh, grounded: true, pressed: false);
        for (int i = 0; i < 5; i++)
        {
            (s, _) = Step(s, grounded: false, pressed: false);
        }

        (_, bool jump) = Step(s, grounded: false, pressed: true);
        Assert.True(jump);
    }

    [Fact]
    public void APressLongAfterLeavingTheEdgeDoesNot()
    {
        (JumpAssist.State s, _) = Step(JumpAssist.Fresh, grounded: true, pressed: false);
        for (int i = 0; i < 20; i++)
        {
            (s, _) = Step(s, grounded: false, pressed: false);
        }

        (_, bool jump) = Step(s, grounded: false, pressed: true);
        Assert.False(jump);
    }

    [Fact]
    public void APressJustBeforeLandingFiresOnTheLandingFrame()
    {
        (JumpAssist.State s, bool jump) = Step(JumpAssist.Fresh, grounded: false, pressed: true);
        Assert.False(jump);
        for (int i = 0; i < 4; i++)
        {
            (s, jump) = Step(s, grounded: false, pressed: false);
            Assert.False(jump);
        }

        (_, jump) = Step(s, grounded: true, pressed: false);
        Assert.True(jump);
    }

    [Fact]
    public void AStalePressDoesNotFireOnLanding()
    {
        (JumpAssist.State s, _) = Step(JumpAssist.Fresh, grounded: false, pressed: true);
        for (int i = 0; i < 20; i++)
        {
            (s, _) = Step(s, grounded: false, pressed: false);
        }

        (_, bool jump) = Step(s, grounded: true, pressed: false);
        Assert.False(jump);
    }

    /// <summary>
    /// ⚠️ The body is still inside its coyote window on the frame it leaves the ground. A jump that
    /// did not spend it could be pressed again in mid-air — a double jump nobody designed.
    /// </summary>
    [Fact]
    public void AJumpSpendsTheCoyoteWindowSoThereIsNoDoubleJump()
    {
        (JumpAssist.State s, bool jump) = Step(JumpAssist.Fresh, grounded: true, pressed: true);
        Assert.True(jump);
        (s, _) = Step(s, grounded: false, pressed: false);
        (_, jump) = Step(s, grounded: false, pressed: true);
        Assert.False(jump);
    }

    [Fact]
    public void APressDuringARollIsHeldUntilTheRollEnds()
    {
        (JumpAssist.State s, bool jump) = Step(JumpAssist.Fresh, grounded: true, pressed: true, canJump: false);
        Assert.False(jump);
        (_, jump) = Step(s, grounded: true, pressed: false, canJump: true);
        Assert.True(jump);
    }

    [Fact]
    public void ZeroWindowsAreTheOldStrictJump()
    {
        (JumpAssist.State s, _) = JumpAssist.Step(JumpAssist.Fresh, true, false, true, Frame, 0f, 0f);
        (s, _) = JumpAssist.Step(s, false, false, true, Frame, 0f, 0f);
        (_, bool late) = JumpAssist.Step(s, false, true, true, Frame, 0f, 0f);
        Assert.False(late);
        (_, bool onGround) = JumpAssist.Step(JumpAssist.Fresh, true, true, true, Frame, 0f, 0f);
        Assert.True(onGround);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void ABadDeltaDoesNotOpenOrPoisonTheWindows(float delta)
    {
        (JumpAssist.State s, _) = JumpAssist.Step(JumpAssist.Fresh, true, false, true, delta, Window, Window);
        Assert.True(float.IsFinite(s.SinceGrounded));
        (_, bool jump) = JumpAssist.Step(s, true, true, true, Frame, Window, Window);
        Assert.True(jump);
    }
}
