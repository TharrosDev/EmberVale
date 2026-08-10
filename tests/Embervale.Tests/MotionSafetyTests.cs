using Embervale.Movement;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The poison guard on the locomotion motor (Phase 37F).
///
/// ⚠️ The case that matters is <see cref="AZeroDirectionTimesABadSpeedIsStillBad"/>: it is the one
/// that actually shipped. A dead enemy stands still — `Stand` passes `Vector3.Zero` — and still threw
/// inside `Mathf.MoveToward`, because `0 * NaN` is NaN and the NaN was in the speed, not the
/// direction. Reading the call site suggested the input could not be at fault; the arithmetic says
/// otherwise.
/// </summary>
public class MotionSafetyTests
{
    [Fact]
    public void OrdinaryVectorsAreFiniteAndPassThroughUnchanged()
    {
        var v = new Vector3(1.5f, -2f, 0f);
        Assert.True(MotionSafety.IsFinite(v));
        Assert.Equal(v, MotionSafety.Sanitize(v));
        Assert.True(MotionSafety.IsFinite(Vector3.Zero));
    }

    [Fact]
    public void NaNInAnyComponentIsNotFinite()
    {
        Assert.False(MotionSafety.IsFinite(new Vector3(float.NaN, 0f, 0f)));
        Assert.False(MotionSafety.IsFinite(new Vector3(0f, float.NaN, 0f)));
        Assert.False(MotionSafety.IsFinite(new Vector3(0f, 0f, float.NaN)));
    }

    [Fact]
    public void InfinityInAnyComponentIsNotFinite()
    {
        Assert.False(MotionSafety.IsFinite(new Vector3(float.PositiveInfinity, 0f, 0f)));
        Assert.False(MotionSafety.IsFinite(new Vector3(0f, float.NegativeInfinity, 0f)));
    }

    [Fact]
    public void SanitizeReplacesABadVectorWithZero()
    {
        Assert.Equal(Vector3.Zero, MotionSafety.Sanitize(new Vector3(float.NaN, 1f, 2f)));
        Assert.Equal(Vector3.Zero, MotionSafety.Sanitize(new Vector3(1f, float.PositiveInfinity, 2f)));
    }

    [Fact]
    public void AZeroDirectionTimesABadSpeedIsStillBad()
    {
        // The shipped failure, in one line: a standing actor with a poisoned MoveSpeed.
        Vector3 target = Vector3.Zero * float.NaN;
        Assert.False(MotionSafety.IsFinite(target));
        Assert.Equal(Vector3.Zero, MotionSafety.Sanitize(target));
    }

    [Fact]
    public void AVeryLargeButFiniteVelocityIsLeftAlone()
    {
        // ⚠️ Deliberately NOT clamped. The guard answers "is this a number", not "is this sensible" —
        // a launch from an explosion is a real gameplay value and clamping it here would quietly
        // change combat feel while pretending to be a crash fix.
        var fast = new Vector3(0f, 900f, 0f);
        Assert.True(MotionSafety.IsFinite(fast));
        Assert.Equal(fast, MotionSafety.Sanitize(fast));
    }
}
