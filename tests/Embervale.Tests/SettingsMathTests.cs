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

    // The appended UI-upgrade fields. Settings itself is a Godot Resource and cannot be built here,
    // so what is pinned is that each field's default passes through its clamp unchanged.

    [Fact]
    public void NewFieldDefaults_AreInsideTheirOwnRanges()
    {
        Assert.Equal(1f, SettingsMath.ClampHudScale(1f));
        Assert.Equal(1f, SettingsMath.ClampHudOpacity(1f));
        Assert.Equal(0f, SettingsMath.ClampHudSafeZone(0f));
        Assert.Equal(1f, SettingsMath.ClampPadSensitivity(1f));
        Assert.Equal(1f, SettingsMath.ClampToastDuration(1f));
        Assert.Equal(1, SettingsMath.ClampSubtitleSize(1));
    }

    // The defaults themselves, read from the source: the one way to pin them without building the
    // Resource. Each is the behaviour before the field existed, and the order is the append order.
    [Fact]
    public void NewFields_DeclareTheDefaultsThatMeanTodaysBehaviour()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "src", "Settings", "Settings.cs"));

        string[] declarations =
        {
            "bool ReadableFont { get; set; } = false;",
            "bool HoldsToPresses { get; set; } = false;",
            "int[] HudElementModes { get; set; } = System.Array.Empty<int>();",
            "float HudScale { get; set; } = 1f;",
            "float HudOpacity { get; set; } = 1f;",
            "float HudSafeZone { get; set; } = 0f;",
            "float PadSensitivityX { get; set; } = 1f;",
            "float PadSensitivityY { get; set; } = 1f;",
            "string[] KeyBindings { get; set; } = System.Array.Empty<string>();",
            "string[] PadBindings { get; set; } = System.Array.Empty<string>();",
            "int SubtitleSize { get; set; } = 1;",
            "float SubtitleBackground { get; set; } = 0.5f;",
            "bool SubtitleSpeakerNames { get; set; } = true;",
            "bool StaticMenuBackground { get; set; } = false;",
            "float ToastDuration { get; set; } = 1f;",
            "int DamageNumberMode { get; set; } = -1;",
        };

        int last = source.IndexOf("bool HighContrast { get; set; } = false;", System.StringComparison.Ordinal);
        Assert.True(last >= 0, "HighContrast, the last field before the appended block, was not found");
        foreach (string declaration in declarations)
        {
            int at = source.IndexOf(declaration, System.StringComparison.Ordinal);
            Assert.True(at >= 0, $"Settings.cs no longer declares: {declaration}");
            Assert.True(at > last, $"Out of append order: {declaration}");
            last = at;
        }
    }

    [Fact]
    public void NewFieldClamps_HoldTheirEnds()
    {
        Assert.Equal(SettingsMath.HudScaleMin, SettingsMath.ClampHudScale(0f));
        Assert.Equal(SettingsMath.HudScaleMax, SettingsMath.ClampHudScale(9f));
        Assert.Equal(SettingsMath.HudOpacityMin, SettingsMath.ClampHudOpacity(0f));
        Assert.Equal(SettingsMath.HudSafeZoneMax, SettingsMath.ClampHudSafeZone(1f));
        Assert.Equal(SettingsMath.PadSensitivityMin, SettingsMath.ClampPadSensitivity(-1f));
        Assert.Equal(SettingsMath.ToastDurationMax, SettingsMath.ClampToastDuration(60f));
        Assert.Equal(0, SettingsMath.ClampSubtitleSize(-4));
        Assert.Equal(SettingsMath.SubtitleSizeMax, SettingsMath.ClampSubtitleSize(7));
    }

    [Fact]
    public void NewFieldClamps_TreatNotANumberAsTheDefault()
    {
        Assert.Equal(1f, SettingsMath.ClampHudScale(float.NaN));
        Assert.Equal(1f, SettingsMath.ClampHudOpacity(float.PositiveInfinity));
        Assert.Equal(0f, SettingsMath.ClampHudSafeZone(float.NaN));
    }

    [Theory]
    [InlineData(-1, true, 1)]
    [InlineData(-1, false, 0)]
    [InlineData(0, true, 0)]
    [InlineData(2, false, 2)]
    [InlineData(3, true, 3)]
    [InlineData(99, true, 1)]
    public void DamageNumberMode_FollowsTheOldToggleUntilItIsSet(int mode, bool legacy, int expected)
    {
        Assert.Equal(expected, SettingsMath.DamageNumberMode(mode, legacy));
    }

    [Fact]
    public void HudElementMode_IsAlwaysForAnythingTheListDoesNotHold()
    {
        Assert.Equal(0, SettingsMath.HudElementMode(null, 3));
        Assert.Equal(0, SettingsMath.HudElementMode(System.Array.Empty<int>(), 0));
        Assert.Equal(2, SettingsMath.HudElementMode(new[] { 0, 2 }, 1));
        Assert.Equal(0, SettingsMath.HudElementMode(new[] { 0, 2 }, 2));
        Assert.Equal(0, SettingsMath.HudElementMode(new[] { 7 }, 0));
        Assert.Equal(0, SettingsMath.HudElementMode(new[] { 1 }, -1));
    }

    [Fact]
    public void BindingFor_ReadsTheLastLineForAnActionAndIgnoresTheRest()
    {
        string[] saved =
        {
            SettingsMath.BindingEntry("jump", "key:32"),
            "broken", "=", "jump=",
            SettingsMath.BindingEntry("jump_high", "key:70"),
            SettingsMath.BindingEntry("jump", "key:74"),
        };

        Assert.Equal("key:74", SettingsMath.BindingFor(saved, "jump"));
        Assert.Equal("key:70", SettingsMath.BindingFor(saved, "jump_high"));
        Assert.Null(SettingsMath.BindingFor(saved, "dodge"));
        Assert.Null(SettingsMath.BindingFor(null, "jump"));
        Assert.Null(SettingsMath.BindingFor(saved, ""));
    }
}
