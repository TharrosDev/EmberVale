using System;
using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The horse's gaits, speed ramp and heading (the Movement upgrade). The rules under test are the
/// ones a player feels without being able to name: a horse gathers itself rather than snapping to
/// speed, and it carves a turn at a gallop that it could pivot through standing still.
/// </summary>
public class MountGaitTests
{
    private const float Frame = 1f / 60f;
    private static readonly GaitTuning T = GaitTuning.Default;

    [Fact]
    public void NoInputIsAHalt()
    {
        Assert.Equal(MountGait.Halt, MountGaits.Select(0f, 0f, false, true, T));
        Assert.Equal(MountGait.Halt, MountGaits.Select(float.NaN, 0f, false, true, T));
    }

    [Theory]
    [InlineData(0.2f, MountGait.Walk)]
    [InlineData(0.6f, MountGait.Trot)]
    [InlineData(1f, MountGait.Canter)]
    public void StickDeflectionPicksTheGait(float input, MountGait expected)
    {
        Assert.Equal(expected, MountGaits.Select(input, 0f, false, gallopGranted: false, T));
    }

    [Fact]
    public void TheGallopIsOnlyEverTheCanterWithThePoolBehindIt()
    {
        Assert.Equal(MountGait.Gallop, MountGaits.Select(1f, 0f, false, gallopGranted: true, T));

        // A half-pressed stick is a trot whatever sprint says — the pool does not skip gaits.
        Assert.Equal(MountGait.Trot, MountGaits.Select(0.6f, 0f, false, gallopGranted: true, T));
    }

    [Fact]
    public void ASharpTurnCollectsTheHorseToATrot()
    {
        float sharp = T.CollectAngle + 0.2f;

        Assert.Equal(MountGait.Trot, MountGaits.Select(1f, sharp, false, gallopGranted: true, T));

        // Collecting never raises a gait.
        Assert.Equal(MountGait.Walk, MountGaits.Select(0.2f, sharp, false, gallopGranted: true, T));
    }

    [Fact]
    public void PullingStraightBackIsTheReinBackAndItGoesBackwards()
    {
        Assert.Equal(MountGait.ReinBack, MountGaits.Select(1f, MathF.PI, backing: true, true, T));
        Assert.True(MountGaits.SpeedOf(MountGait.ReinBack, T) < 0f);
    }

    [Fact]
    public void GaitSpeedsRiseStrictlyWithTheGait()
    {
        float previous = float.NegativeInfinity;
        foreach (MountGait gait in Enum.GetValues<MountGait>())
        {
            float speed = MountGaits.SpeedOf(gait, T);
            Assert.True(speed > previous, $"{gait} should be faster than the gait below it");
            previous = speed;
        }
    }

    /// <summary>The gallop keeps 39A's promise: 1.7 x 1.6 of a walking rider.</summary>
    [Fact]
    public void TheDefaultGallopIsTheOneThirtyNineAPromised()
    {
        Assert.Equal(1.7f * 1.6f, T.GallopSpeed, 3);
        Assert.Equal(1.7f, T.CanterSpeed, 3);
    }

    [Fact]
    public void SpeedRampsUpGentlyAndDownHarder()
    {
        float up = MountGaits.StepSpeed(0f, T.GallopSpeed, Frame, T);
        float down = T.GallopSpeed - MountGaits.StepSpeed(T.GallopSpeed, 0f, Frame, T);

        Assert.True(up > 0f && up < T.GallopSpeed, "one frame must not reach a gallop");
        Assert.True(down > up, "braking is firmer than gathering pace");
    }

    [Fact]
    public void ABalkingHorsePlantsItsFeet()
    {
        float normal = MountGaits.StepSpeed(T.GallopSpeed, 0f, Frame, T);
        float balk = MountGaits.StepSpeed(T.GallopSpeed, 0f, Frame, T, balking: true);

        Assert.True(balk < normal);
    }

    [Fact]
    public void TheRampArrivesExactlyAndDoesNotOvershoot()
    {
        float speed = 0f;
        for (int i = 0; i < 600; i++)
        {
            speed = MountGaits.StepSpeed(speed, T.CanterSpeed, Frame, T);
            Assert.True(speed <= T.CanterSpeed);
        }

        Assert.Equal(T.CanterSpeed, speed);
    }

    /// <summary>A horse going forward stops before it backs — it never flips velocity in one step,
    /// and the half of the reversal spent stopping uses the braking rate.</summary>
    [Fact]
    public void AReversalStopsFirst()
    {
        float speed = MountGaits.StepSpeed(0.02f, -T.ReinBackSpeed, Frame, T);

        Assert.Equal(0f, speed);
        Assert.True(MountGaits.StepSpeed(speed, -T.ReinBackSpeed, Frame, T) < 0f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANonFiniteSpeedCannotPoisonTheRamp(float bad)
    {
        Assert.True(float.IsFinite(MountGaits.StepSpeed(bad, T.WalkSpeed, Frame, T)));
        Assert.Equal(1f, MountGaits.StepSpeed(1f, T.WalkSpeed, bad, T));
    }

    [Fact]
    public void AGallopingHorseTurnsSlowerThanAStandingOne()
    {
        Assert.Equal(T.PivotTurnRate, MountGaits.TurnRate(0f, T), 4);
        Assert.Equal(T.GallopTurnRate, MountGaits.TurnRate(T.GallopSpeed, T), 4);
        Assert.True(MountGaits.TurnRate(T.CanterSpeed, T) < MountGaits.TurnRate(T.WalkSpeed, T));
        Assert.Equal(T.GallopTurnRate, MountGaits.TurnRate(99f, T), 4); // clamped past the gallop
    }

    [Fact]
    public void TheHeadingSwingsTheShortWayAndStopsOnTarget()
    {
        // From just left of behind to just right of it is a short hop across ±π, not a full circle.
        float heading = MountGaits.StepHeading(MathF.PI - 0.05f, -MathF.PI + 0.05f, 10f, Frame);
        Assert.True(MountGaits.AngleBetween(heading, -MathF.PI + 0.05f) < 0.001f);

        float slow = MountGaits.StepHeading(0f, 1f, 1f, 0.1f);
        Assert.Equal(0.1f, slow, 4);
    }

    [Fact]
    public void WrapAndYawAgreeWithGodotsForward()
    {
        Assert.Equal(0f, MountGaits.YawOf(0f, -1f), 4);            // -Z is forward
        Assert.Equal(MathF.PI / 2f, MountGaits.YawOf(-1f, 0f), 4); // a quarter turn left faces -X
        Assert.Equal(-MathF.PI + 0.5f, MountGaits.Wrap(MathF.PI + 0.5f), 4);
        Assert.Equal(0f, MountGaits.Wrap(float.NaN));
    }

    [Fact]
    public void AHorseJumpsFromImpulsionOnly()
    {
        Assert.False(MountGaits.CanJump(MountGait.Halt));
        Assert.False(MountGaits.CanJump(MountGait.Walk));
        Assert.False(MountGaits.CanJump(MountGait.ReinBack));
        Assert.True(MountGaits.CanJump(MountGait.Trot));
        Assert.True(MountGaits.CanJump(MountGait.Gallop));
    }

    [Fact]
    public void StandingRestsTheHorseFasterThanCantering()
    {
        Assert.True(MountGaits.RegenScale(MountGait.Halt) > MountGaits.RegenScale(MountGait.Trot));
        Assert.True(MountGaits.RegenScale(MountGait.Trot) > MountGaits.RegenScale(MountGait.Canter));
        Assert.True(MountGaits.RegenScale(MountGait.Canter) > 0f);
    }
}
