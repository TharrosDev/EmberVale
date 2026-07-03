using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the pure curve maths behind the 30.5I microinteractions (panel fades, toast slides,
/// the XP/level-up pops). The key invariant is the reduced-motion collapse: a non-positive
/// duration must report complete immediately so every animation snaps to its settled state.
/// </summary>
public class UiMotionTests
{
    [Fact]
    public void Progress_ZeroDuration_IsCompleteImmediately()
    {
        Assert.Equal(1f, UiMotion.Progress(0f, 0f));
        Assert.Equal(1f, UiMotion.Progress(0f, -1f));
    }

    [Fact]
    public void Progress_ClampsToUnitRange()
    {
        Assert.Equal(0f, UiMotion.Progress(-0.5f, 1f));
        Assert.Equal(0.5f, UiMotion.Progress(0.5f, 1f));
        Assert.Equal(1f, UiMotion.Progress(2f, 1f));
    }

    [Fact]
    public void EaseOut_AnchorsAndShape()
    {
        Assert.Equal(0f, UiMotion.EaseOut(0f));
        Assert.Equal(1f, UiMotion.EaseOut(1f));
        Assert.True(UiMotion.EaseOut(0.5f) > 0.5f); // fast start
        Assert.Equal(1f, UiMotion.EaseOut(2f));     // clamped past the end
    }

    [Fact]
    public void EaseIn_AnchorsAndShape()
    {
        Assert.Equal(0f, UiMotion.EaseIn(0f));
        Assert.Equal(1f, UiMotion.EaseIn(1f));
        Assert.True(UiMotion.EaseIn(0.5f) < 0.5f); // gentle start
        Assert.Equal(0f, UiMotion.EaseIn(-1f));    // clamped before the start
    }
}
