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

    [Fact]
    public void HoldStep_FillsWhileHeldAndStopsAtFull()
    {
        float progress = 0f;
        for (int frame = 0; frame < 30; frame++)
        {
            float next = UiMotion.HoldStep(progress, held: true, 1f / 60f, holdSeconds: 1f, drainSeconds: 0.2f);
            Assert.True(next >= progress);
            progress = next;
        }

        Assert.Equal(0.5f, progress, 3);
        Assert.Equal(1f, UiMotion.HoldStep(0.99f, held: true, 1f, 1f, 0.2f));
    }

    [Fact]
    public void HoldStep_DrainsWhenReleasedAndStopsAtEmpty()
    {
        Assert.Equal(0.25f, UiMotion.HoldStep(0.5f, held: false, 0.05f, 1f, drainSeconds: 0.2f), 3);
        Assert.Equal(0f, UiMotion.HoldStep(0.1f, held: false, 1f, 1f, 0.2f));
    }

    [Fact]
    public void HoldStep_CollapsedDurationsAreInstant()
    {
        Assert.Equal(1f, UiMotion.HoldStep(0f, held: true, 0f, holdSeconds: 0f, drainSeconds: 0.2f));
        Assert.Equal(0f, UiMotion.HoldStep(0.9f, held: false, 0f, 1f, drainSeconds: 0f));
    }

    [Fact]
    public void HoldStep_IgnoresANegativeFrameTime()
    {
        Assert.Equal(0.4f, UiMotion.HoldStep(0.4f, held: true, -1f, 1f, 0.2f));
        Assert.Equal(0.4f, UiMotion.HoldStep(0.4f, held: false, -1f, 1f, 0.2f));
    }
}
