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

    [Fact]
    public void TheBodyIsSeeThroughAndTheRimIsNot()
    {
        // About a third at the blow: the ground and whoever stands on the warning read through it.
        Assert.InRange(TelegraphMath.FillAlpha(1f, false), 0.3f, 0.42f);
        Assert.True(TelegraphMath.FillAlpha(0f, false) > 0.15f, "visible the instant it arms");
        Assert.True(TelegraphMath.FillAlpha(1f, false) > TelegraphMath.FillAlpha(0f, false));

        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            Assert.True(TelegraphMath.RimAlpha(t, false) > TelegraphMath.FillAlpha(t, false) + 0.15f,
                "the rim is what draws the shape; it must always stand well clear of the body");
            Assert.True(TelegraphMath.RimAlpha(t, false) <= 1f);
        }

        Assert.Equal(1f, TelegraphMath.RimAlpha(1f, false), 5);
    }

    [Fact]
    public void HighContrastIsASolidShapeAndReducedMotionHoldsItStill()
    {
        Assert.Equal(1f, TelegraphMath.RimAlpha(0f, true), 5);
        Assert.True(TelegraphMath.FillAlpha(0f, true) > TelegraphMath.FillAlpha(1f, false));
        Assert.Equal(TelegraphMath.FillAlpha(0f, true), TelegraphMath.FillAlpha(1f, true), 5);

        // An unblockable pulses; with motion off it does not, and nothing else ever did.
        bool pulsed = false;
        for (int i = 0; i < 40; i++)
        {
            float seconds = i * 0.03f;
            pulsed |= TelegraphMath.ClassPulse(TelegraphClass.Unblockable, seconds) < 0.99f;
            Assert.Equal(1f, TelegraphMath.ClassPulse(TelegraphClass.Unblockable, seconds, motion: false), 5);
            Assert.Equal(1f, TelegraphMath.ClassPulse(TelegraphClass.Sweep, seconds), 5);
        }

        Assert.True(pulsed);
    }

    [Fact]
    public void TheRimIsALineOnTheGroundNotAShareOfTheCreature()
    {
        // A man-sized ring (2.2 m, band 0.22 of it) keeps the rim it always had.
        Assert.Equal(0.55f, TelegraphMath.RimShare(2.2f * 0.22f, 0.55f), 5);

        // Round a dragon the same share would be over a metre of solid colour: it is 0.3 m instead.
        float band = 10f * 0.22f;
        Assert.Equal(TelegraphMath.RimMetres, TelegraphMath.RimShare(band, 0.55f) * band, 3);

        // Never a hair, and nonsense in changes nothing.
        Assert.Equal(0.02f, TelegraphMath.RimShare(40f, 0.1f), 5);
        Assert.Equal(0.55f, TelegraphMath.RimShare(0f, 0.55f), 5);
        Assert.Equal(0.55f, TelegraphMath.RimShare(float.NaN, 0.55f), 5);
    }
}
