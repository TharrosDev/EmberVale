using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the wind-up telegraph's curve (Phase 36C). The ring itself is engine geometry, but the shape
/// of its growth is the thing a player actually reads to time a dodge or a punish — and a curve that
/// grows linearly, or peaks at the wrong moment, teaches the wrong spacing while looking fine in a
/// screenshot.
/// </summary>
public class TelegraphTests
{
    [Fact]
    public void TheRingStartsAtNothingAndEndsAtFull()
    {
        Assert.Equal(0f, TelegraphMath.RingScale(0f), 5);
        Assert.Equal(1f, TelegraphMath.RingScale(1f), 5);
    }

    [Fact]
    public void TheRingOpensFastAndSettlesLate()
    {
        // Ease-out: past halfway the ring is already most of its final size, so the last moments
        // read as "about to land" rather than "still growing".
        Assert.Equal(0.75f, TelegraphMath.RingScale(0.5f), 5);
        Assert.True(TelegraphMath.RingScale(0.25f) > 0.25f, "growth must front-load, not be linear");
    }

    [Fact]
    public void TheRingNeverShrinks()
    {
        float previous = -1f;
        for (int i = 0; i <= 20; i++)
        {
            float scale = TelegraphMath.RingScale(i / 20f);
            Assert.True(scale >= previous, "the warning must only ever grow");
            previous = scale;
        }
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(9f, 1f)]
    public void TheCurveIsClampedOutsideTheWindow(float t, float expected)
    {
        Assert.Equal(expected, TelegraphMath.RingScale(t), 5);
    }

    [Fact]
    public void OpacityRisesTowardTheBlow()
    {
        // Most insistent at the moment it matters — the frame before the hitbox opens.
        Assert.True(TelegraphMath.RingAlpha(1f) > TelegraphMath.RingAlpha(0f));
        Assert.True(TelegraphMath.RingAlpha(0f) > 0f, "the ring must be visible the instant it arms");
        Assert.True(TelegraphMath.RingAlpha(1f) <= 1f);
    }

    [Fact]
    public void OpacityIsClamped()
    {
        Assert.Equal(TelegraphMath.RingAlpha(0f), TelegraphMath.RingAlpha(-3f), 5);
        Assert.Equal(TelegraphMath.RingAlpha(1f), TelegraphMath.RingAlpha(4f), 5);
    }
}
