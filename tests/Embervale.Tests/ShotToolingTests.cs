using System;
using System.Collections.Generic;
using System.Text.Json;
using Embervale.Bootstrap;
using Embervale.Debugging;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The engine-free cores of the screenshot harnesses: shot selection, pixel measurements, the
/// filmstrip layout, the run manifest and the one-off shot request.
/// </summary>
public class ShotToolingTests
{
    // --- selection ----------------------------------------------------------------------------------

    [Fact]
    public void NoPatternsSelectsEveryShot()
    {
        var filter = new ShotFilter(new[] { " ", string.Empty });
        Assert.False(filter.Active);
        Assert.True(filter.Matches("00-map"));
    }

    [Theory]
    [InlineData("00-map", "00-map", true)]
    [InlineData("00-MAP", "00-map", true)]
    [InlineData("00-map", "00-map-detail", false)]
    [InlineData("*map*", "00-map-detail", true)]
    [InlineData("0?-map", "03-map", true)]
    [InlineData("0?-map", "13-map", false)]
    [InlineData("*_tp_impact", "fireball_tp_impact", true)]
    [InlineData("*_tp_impact", "fireball_tpday_impact", false)]
    [InlineData("*", "", true)]
    [InlineData("a*b*c", "a-b-b-c", true)]
    [InlineData("a*b*c", "a-b-b", false)]
    public void GlobMatchesWholeNames(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, ShotFilter.Glob(pattern, name));
    }

    [Fact]
    public void AFilterReportsPatternsThatSelectNothing()
    {
        var filter = new ShotFilter(new[] { "01-*", "jurnal", "03-inventory" });
        string[] names = { "00-map", "01-journal", "03-inventory" };
        Assert.True(filter.Active);
        Assert.True(filter.Matches("01-journal"));
        Assert.False(filter.Matches("00-map"));
        Assert.Equal(new[] { "jurnal" }, filter.Unmatched(names));
    }

    // --- pixels -------------------------------------------------------------------------------------

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }

        return pixels;
    }

    [Fact]
    public void ABlackFrameIsFlatAndBlack()
    {
        ShotStats stats = ShotStats.Compute(Solid(8, 4, 0, 0, 0), 8, 4);
        Assert.True(stats.Flat);
        Assert.Equal(100.0, stats.BlackPct);
        Assert.Equal(new[] { "flat", "black" }, stats.Flags(null, null));
    }

    [Fact]
    public void MissingMaterialMagentaIsCounted()
    {
        byte[] pixels = Solid(10, 10, 40, 90, 60);
        for (int i = 0; i < 5; i++)
        {
            pixels[i * 3] = 255;
            pixels[(i * 3) + 1] = 0;
            pixels[(i * 3) + 2] = 255;
        }

        ShotStats stats = ShotStats.Compute(pixels, 10, 10);
        Assert.Equal(5.0, stats.MagentaPct);
        Assert.False(stats.Flat);
        Assert.Contains("magenta", stats.Flags(null, null));
    }

    [Fact]
    public void AnIdenticalFrameNamesTheShotItRepeats()
    {
        byte[] pixels = Solid(6, 6, 10, 200, 30);
        pixels[0] = 99;
        ShotStats first = ShotStats.Compute(pixels, 6, 6);
        ShotStats again = ShotStats.Compute((byte[])pixels.Clone(), 6, 6);
        pixels[5] = 7;
        ShotStats changed = ShotStats.Compute(pixels, 6, 6);

        Assert.Contains("same_as:01-open", again.Flags(first, "01-open"));
        Assert.DoesNotContain(changed.Flags(first, "01-open"), flag => flag.StartsWith("same_as", StringComparison.Ordinal));
    }

    [Fact]
    public void StatsOfAHalfAndHalfFrame()
    {
        byte[] pixels = Solid(2, 1, 0, 0, 0);
        pixels[3] = pixels[4] = pixels[5] = 200;
        ShotStats stats = ShotStats.Compute(pixels, 2, 1);
        Assert.Equal(100.0, stats.Mean);
        Assert.Equal(100.0, stats.Std);
        Assert.Equal(0, stats.Min);
        Assert.Equal(200, stats.Max);
        Assert.Equal(50.0, stats.BlackPct);
        Assert.Empty(stats.Flags(null, null));
    }

    [Fact]
    public void TooFewPixelsIsAnEmptyMeasurementNotACrash()
    {
        ShotStats stats = ShotStats.Compute(new byte[5], 4, 4);
        Assert.True(stats.Flat);
    }

    // --- film ---------------------------------------------------------------------------------------

    [Fact]
    public void TwelveFramesMakeAFourByThreeSheet()
    {
        FilmLayout layout = FilmLayout.For(12, 1280, 720);
        Assert.Equal((4, 3, 320, 180), (layout.Columns, layout.Rows, layout.CellWidth, layout.CellHeight));
        Assert.Equal((4 * 320) + (5 * 2), layout.Width);
        Assert.Equal((3 * 180) + (4 * 2), layout.Height);
        Assert.Equal((2, 2), layout.Cell(0));
        Assert.Equal((2 + 322, 2), layout.Cell(1));
        Assert.Equal((2, 2 + 182), layout.Cell(4));
        Assert.Equal(320, layout.BarWidth(11));
        Assert.True(layout.BarWidth(0) < layout.BarWidth(5));
    }

    [Fact]
    public void AShortStripIsOneRow()
    {
        FilmLayout layout = FilmLayout.For(3, 640, 360);
        Assert.Equal((3, 1), (layout.Columns, layout.Rows));
        FilmLayout five = FilmLayout.For(5, 640, 360);
        Assert.Equal((4, 2), (five.Columns, five.Rows));
    }

    [Theory]
    [InlineData(null, true, 12, 4)]
    [InlineData("", true, 12, 4)]
    [InlineData("16x3", true, 16, 3)]
    [InlineData("8", true, 8, 4)]
    [InlineData("500x900", true, 48, 120)]
    [InlineData("1x0", true, 2, 1)]
    [InlineData("fast", false, 12, 4)]
    [InlineData("4x2x1", false, 12, 4)]
    public void FilmArgumentParses(string? text, bool ok, int frames, int stride)
    {
        Assert.Equal(ok, FilmLayout.TryParse(text, out int gotFrames, out int gotStride));
        Assert.Equal((frames, stride), (gotFrames, gotStride));
    }

    // --- manifest -----------------------------------------------------------------------------------

    [Fact]
    public void TheManifestSeparatesCapturedSkippedAndFailedShots()
    {
        var manifest = new ShotManifest("--panelshots") { Width = 1280, Height = 720, Only = new[] { "01-*" } };
        manifest.Add("00-map", selected: false);
        ShotRecord good = manifest.Add("01-journal", selected: true);
        good.File = "01-journal.png";
        good.Thumb = "01-journal.thumb.jpg";
        good.Frame = 140;
        good.Stats = ShotStats.Compute(Solid(2, 2, 0, 0, 0), 2, 2);
        good.Flags.Add("black");
        ShotRecord bad = manifest.Add("01-journal-empty", selected: true);
        bad.Problem = "'01-journal-empty' prerequisite failed: no journal";
        manifest.FocusLost.Add("01-journal");

        Assert.False(manifest.Passed);
        Assert.Equal(1, manifest.Captured);
        Assert.Equal(new[] { "01-journal-empty" }, manifest.Failed);
        Assert.Equal(new[] { "01-journal" }, manifest.Flagged);

        using JsonDocument json = JsonDocument.Parse(manifest.ToJson(3.14159));
        JsonElement root = json.RootElement;
        Assert.Equal(ShotManifest.Schema, root.GetProperty("schema").GetInt32());
        Assert.Equal("--panelshots", root.GetProperty("suite").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(3.14, root.GetProperty("seconds").GetDouble());
        Assert.Equal(3, root.GetProperty("registered").GetInt32());
        Assert.Equal(1, root.GetProperty("captured").GetInt32());
        Assert.Equal("01-*", root.GetProperty("only")[0].GetString());
        Assert.Equal("01-journal", root.GetProperty("focus_lost")[0].GetString());
        JsonElement shots = root.GetProperty("shots");
        Assert.False(shots[0].GetProperty("selected").GetBoolean());
        Assert.False(shots[0].TryGetProperty("file", out _));
        Assert.Equal("01-journal.png", shots[1].GetProperty("file").GetString());
        Assert.Equal(100.0, shots[1].GetProperty("stats").GetProperty("black_pct").GetDouble());
        Assert.Equal("black", shots[1].GetProperty("flags")[0].GetString());
        Assert.False(shots[2].GetProperty("ok").GetBoolean());
        Assert.Contains("no journal", shots[2].GetProperty("problem").GetString());
    }

    [Fact]
    public void ARunWithOnlyRunLevelFailuresDoesNotPass()
    {
        var manifest = new ShotManifest("--spellshots");
        manifest.Add("a", selected: true).File = "a.png";
        Assert.True(manifest.Passed);
        manifest.Failures.Add("2 problem(s) in this run");
        Assert.False(manifest.Passed);
    }

    // --- the one-off request ------------------------------------------------------------------------

    [Fact]
    public void FlagsDescribeAWorldView()
    {
        var args = new CommandLineArgs(new[]
        {
            "--shot", "--name=market_dusk", "--cell=ember_crown.embermarket", "--yaw=90", "--pitch", "-25",
            "--distance=30", "--hour=19.5", "--weather=weather.storm", "--hud", "--settle=10",
        });
        OneShotSpec spec = OneShotSpec.FromArgs(args);
        Assert.Null(spec.Validate());
        Assert.True(spec.IsWorld);
        Assert.Equal("market_dusk", spec.Name);
        Assert.Equal("ember_crown.embermarket", spec.Cell);
        Assert.Equal((90f, -25f, 30f), (spec.Yaw, spec.Pitch, spec.Distance));
        Assert.Equal(19.5f, spec.Hour);
        Assert.Equal("weather.storm", spec.Weather);
        Assert.True(spec.Hud);
        Assert.False(spec.ShowPlayer);
        Assert.Equal(10, spec.Settle);
        Assert.Equal("free", spec.View);
    }

    [Fact]
    public void YawZeroLooksNorthAndNegativePitchLooksDown()
    {
        var north = new OneShotSpec { Yaw = 0f, Pitch = 0f, Distance = 10f };
        (float x, float y, float z) = north.Forward();
        Assert.Equal(0f, x, 4);
        Assert.Equal(0f, y, 4);
        Assert.Equal(-1f, z, 4);
        (float ox, float oy, float oz) = north.CameraOffset();
        Assert.Equal(0f, ox, 3);
        Assert.Equal(0f, oy, 3);
        Assert.Equal(10f, oz, 3);

        var east = new OneShotSpec { Yaw = 90f, Pitch = -30f, Distance = 10f };
        (x, y, z) = east.Forward();
        Assert.True(x > 0.8f);
        Assert.True(y < -0.49f);
        Assert.Equal(0f, z, 4);
        (_, oy, _) = east.CameraOffset();
        Assert.True(oy > 4.9f, "a camera looking down stands above what it looks at");

        var straightDown = new OneShotSpec { Pitch = -90f };
        (x, _, z) = straightDown.Forward();
        Assert.True(Math.Abs(x) + Math.Abs(z) > 0.01f, "the view keeps a heading so the camera has an up");
    }

    [Fact]
    public void JsonHoldsOneShotOrMany()
    {
        List<OneShotSpec> one = OneShotSpec.FromJson(
            "{\"name\":\"gate\",\"at\":[12.5,-40],\"hide_hud\":false,\"view\":\"TP\",\"hour\":6}", out string? error);
        Assert.Null(error);
        Assert.Single(one);
        Assert.Equal(new[] { 12.5f, -40f }, one[0].At);
        Assert.True(one[0].Hud);
        Assert.Equal("tp", one[0].View);
        Assert.Equal(6f, one[0].Hour);
        Assert.Null(one[0].Validate());

        List<OneShotSpec> many = OneShotSpec.FromJson(
            "{\"shots\":[{\"cell\":\"a.b\"},{\"location\":\"location.x\",\"show-player\":true}, {\"name\":\"named\"}]}", out error);
        Assert.Null(error);
        Assert.Equal(new[] { "shot_01", "shot_02", "named" }, many.ConvertAll(spec => spec.Name));
        Assert.True(many[1].ShowPlayer);

        List<OneShotSpec> array = OneShotSpec.FromJson("[{\"spell\":\"fireball\",\"phase\":\"Linger\"}]", out error);
        Assert.Null(error);
        Assert.True(array[0].IsSpell);
        Assert.Equal("linger", array[0].Phase);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("42")]
    [InlineData("[1,2]")]
    [InlineData("[]")]
    public void BadJsonIsAnErrorNotAShot(string json)
    {
        List<OneShotSpec> specs = OneShotSpec.FromJson(json, out string? error);
        Assert.NotNull(error);
        Assert.Empty(specs);
    }

    [Fact]
    public void AUiRequestNamesASuiteAndItsShots()
    {
        var spec = new OneShotSpec { Ui = "panelshots/00-map, 05-*" };
        Assert.Null(spec.Validate());
        Assert.True(spec.IsUi);
        Assert.Equal("--panelshots", spec.UiSuiteFlag);
        Assert.Equal(new[] { "00-map", "05-*" }, spec.UiShots);
        Assert.Equal("--combat-shots", new OneShotSpec { Ui = "--combat-shots/x" }.UiSuiteFlag);
    }

    [Fact]
    public void RequestsThatCannotWorkAreRefused()
    {
        Assert.NotNull(new OneShotSpec { Name = "a/b" }.Validate());
        Assert.NotNull(new OneShotSpec { Ui = "panelshots" }.Validate());
        Assert.NotNull(new OneShotSpec { Ui = "panelshots/x", Spell = "fireball" }.Validate());
        Assert.NotNull(new OneShotSpec { Spell = "fireball", Phase = "middle" }.Validate());
        Assert.NotNull(new OneShotSpec { View = "orbit" }.Validate());
        Assert.NotNull(new OneShotSpec { At = new[] { 1f } }.Validate());
        Assert.NotNull(new OneShotSpec { At = new[] { 1f, float.NaN } }.Validate());
        Assert.NotNull(new OneShotSpec { Hour = 25f }.Validate());
        Assert.NotNull(new OneShotSpec { Fov = 5f }.Validate());
        Assert.Null(new OneShotSpec { At = new[] { 1f, 2f, 3f }, Hour = 0f }.Validate());
    }
}
