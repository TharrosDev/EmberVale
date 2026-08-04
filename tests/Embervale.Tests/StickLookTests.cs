using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the analog-stick look curve. Unlike the mouse, a stick reports a sustained deflection
/// rather than a movement, so the step has to be a rate integrated against the frame time — and
/// getting that wrong is invisible until someone plays at a different framerate.
/// </summary>
public class StickLookTests
{
    private const float Deadzone = 0.15f;
    private const float Rate = 2.6f;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.1f)]
    [InlineData(0.15f)]
    [InlineData(-0.15f)]
    public void InsideTheDeadzone_ProducesNoStep(float axis)
    {
        Assert.Equal(0f, SettingsMath.StickLookStep(axis, Deadzone, Rate, 0.016f, 1f), 6);
    }

    [Fact]
    public void AtTheDeadzoneEdge_TheStepStartsFromZero()
    {
        // No jump the instant the stick crosses the threshold — the remainder is rescaled from 0.
        float step = SettingsMath.StickLookStep(0.1501f, Deadzone, Rate, 0.016f, 1f);

        Assert.True(step > 0f);
        Assert.True(step < 0.0001f, "the step just past the deadzone must be near zero, not a jump");
    }

    [Fact]
    public void FullDeflection_TurnsAtTheStatedRate()
    {
        Assert.Equal(Rate * 0.5f, SettingsMath.StickLookStep(1f, Deadzone, Rate, 0.5f, 1f), 5);
    }

    [Fact]
    public void TheStepIsFramerateIndependent()
    {
        // One 1/30 s frame must turn exactly as far as two 1/60 s frames, or a controller aims
        // differently on a slower machine.
        float coarse = SettingsMath.StickLookStep(0.8f, Deadzone, Rate, 1f / 30f, 1f);
        float fine = SettingsMath.StickLookStep(0.8f, Deadzone, Rate, 1f / 60f, 1f) * 2f;

        Assert.Equal(coarse, fine, 6);
    }

    [Fact]
    public void SignFollowsTheStick()
    {
        float right = SettingsMath.StickLookStep(0.7f, Deadzone, Rate, 0.016f, 1f);
        float left = SettingsMath.StickLookStep(-0.7f, Deadzone, Rate, 0.016f, 1f);

        Assert.True(right > 0f);
        Assert.Equal(-right, left, 6);
    }

    [Fact]
    public void TheResponseIsCurvedSoFineAimNearCentreIsPossible()
    {
        // Squared magnitude: half deflection must turn well under half as fast, or the stick has no
        // usable slow range at all.
        float half = SettingsMath.StickLookStep(0.575f, Deadzone, Rate, 0.016f, 1f); // midpoint of the live travel
        float full = SettingsMath.StickLookStep(1f, Deadzone, Rate, 0.016f, 1f);

        Assert.Equal(full * 0.25f, half, 5);
    }

    [Fact]
    public void TheSensitivityMultiplierScalesTheStep()
    {
        float once = SettingsMath.StickLookStep(0.9f, Deadzone, Rate, 0.016f, 1f);
        float twice = SettingsMath.StickLookStep(0.9f, Deadzone, Rate, 0.016f, 2f);

        Assert.Equal(once * 2f, twice, 6);
    }

    [Fact]
    public void ANonPositiveDeltaProducesNoStep()
    {
        Assert.Equal(0f, SettingsMath.StickLookStep(1f, Deadzone, Rate, 0f, 1f), 6);
    }
}
