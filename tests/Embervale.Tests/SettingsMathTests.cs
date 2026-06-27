using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure conversions behind <c>SettingsService</c>'s audio application (Phase 24E). The
/// linear-fader → decibel mapping drives every mixer bus volume, so its endpoints (unity, mute) and
/// the silence floor are pinned here; the service's disk/engine application runs in-engine.
/// </summary>
public class SettingsMathTests
{
    [Fact]
    public void LinearToDb_UnityIsZeroDb()
    {
        Assert.Equal(0f, SettingsMath.LinearToDb(1f), 3);
    }

    [Fact]
    public void LinearToDb_HalfIsAboutMinusSixDb()
    {
        Assert.Equal(-6.02f, SettingsMath.LinearToDb(0.5f), 2);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(0.00001f)]
    public void LinearToDb_SilenceFloorsRatherThanGoingToNegativeInfinity(float linear)
    {
        Assert.Equal(SettingsMath.SilenceDb, SettingsMath.LinearToDb(linear));
    }

    [Fact]
    public void LinearToDb_IsMonotonicAcrossTheFader()
    {
        float previous = SettingsMath.LinearToDb(0.01f);
        for (float v = 0.02f; v <= 1f; v += 0.01f)
        {
            float db = SettingsMath.LinearToDb(v);
            Assert.True(db >= previous, $"db should not decrease as volume rises ({v})");
            previous = db;
        }
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2f, 1f)]
    public void ClampVolume_ConstrainsToFaderRange(float input, float expected)
    {
        Assert.Equal(expected, SettingsMath.ClampVolume(input));
    }

    // --- Look (Phase 25.5D: wire the 24F mouse settings into the controller) ----

    [Fact]
    public void LookStep_DefaultMultiplierLeavesBaseUnchanged()
    {
        // multiplier 1.0 (the Settings default) must reproduce the old hardcoded feel exactly.
        Assert.Equal(100f * 0.0028f, SettingsMath.LookStep(100f, 0.0028f, 1f), 6);
    }

    [Fact]
    public void LookStep_ScalesWithMultiplier()
    {
        Assert.Equal(2f * (10f * 0.0028f), SettingsMath.LookStep(10f, 0.0028f, 2f), 6);
    }

    [Fact]
    public void ApplyPitch_NormalSubtractsStep()
    {
        Assert.Equal(0.4f, SettingsMath.ApplyPitch(0.5f, 0.1f, invertY: false, 1.45f), 5);
    }

    [Fact]
    public void ApplyPitch_InvertYAddsStep()
    {
        Assert.Equal(0.6f, SettingsMath.ApplyPitch(0.5f, 0.1f, invertY: true, 1.45f), 5);
    }

    [Fact]
    public void ApplyPitch_ClampsToLimit()
    {
        Assert.Equal(1.45f, SettingsMath.ApplyPitch(1.4f, -1f, invertY: false, 1.45f), 5);  // looking far up
        Assert.Equal(-1.45f, SettingsMath.ApplyPitch(-1.4f, 1f, invertY: false, 1.45f), 5); // far down
    }
}
