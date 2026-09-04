using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The AI level-of-detail clock, and the six-minute provoke memory it exists to prevent.
/// </summary>
public class AiLodClockTests
{
    private const double Frame = 1d / 60d;
    private const double SleepInterval = 0.5d;

    [Fact]
    public void TheFirstFarFrameWakesTheActor()
    {
        var clock = default(AiLodClock);

        // The sleep timer starts at zero, so a distant actor thinks once and then sleeps.
        Assert.False(clock.ShouldSleep(Frame, SleepInterval));
    }

    [Fact]
    public void SubsequentFramesSleepUntilTheIntervalElapses()
    {
        var clock = default(AiLodClock);
        clock.ShouldSleep(Frame, SleepInterval); // the waking frame

        int slept = 0;
        while (clock.ShouldSleep(Frame, SleepInterval))
        {
            slept++;
            Assert.True(slept < 1000, "the clock never woke");
        }

        // Half a second at 60 Hz, less the frame that started the interval.
        Assert.InRange(slept, 28, 30);
    }

    [Fact]
    public void SkippedTimeIsBankedNotLost()
    {
        // ⚠️ THE BUG THIS EXISTS FOR. Without banking, the wall-clock timers advance by one FRAME
        // per sleep interval instead of by the interval — so a distant enemy's 12 s provoke memory
        // ran for six real minutes and it never stood down.
        var clock = default(AiLodClock);
        clock.ShouldSleep(Frame, SleepInterval);

        double realSeconds = Frame;
        while (clock.ShouldSleep(Frame, SleepInterval))
        {
            realSeconds += Frame;
        }

        double wall = clock.ConsumeWallSeconds(Frame);
        realSeconds += Frame;

        // The waking tick is handed every second that actually passed, not one frame's worth.
        Assert.Equal(realSeconds - Frame, wall, precision: 6);
        Assert.True(wall > SleepInterval * 0.9d, $"only {wall:F3} s of {realSeconds:F3} s reached the brain");
    }

    [Fact]
    public void ConsumingTheBankEmptiesIt()
    {
        var clock = default(AiLodClock);
        clock.ShouldSleep(Frame, SleepInterval);
        clock.ShouldSleep(Frame, SleepInterval);

        Assert.True(clock.Banked > 0d);
        clock.ConsumeWallSeconds(Frame);
        Assert.Equal(0d, clock.Banked);

        // A second consume in the same tick would under-count, which is why it is called once.
        Assert.Equal(Frame, clock.ConsumeWallSeconds(Frame), precision: 9);
    }

    [Fact]
    public void AnActorInRangeIsHandedItsOwnFrameDelta()
    {
        // Never asked to sleep: the wall clock and the frame delta agree exactly.
        var clock = default(AiLodClock);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(Frame, clock.ConsumeWallSeconds(Frame), precision: 9);
        }
    }
}
