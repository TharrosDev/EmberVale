using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the shell front's decisions: the splash and first-run setup never reach a tool run, the
/// title's painting follows the newest save's act, and the credits roll stays inside its ends.
/// </summary>
public class ShellFrontRulesTests
{
    [Theory]
    [InlineData(false, false, 0, true)]
    [InlineData(true, false, 0, false)]  // headless gates
    [InlineData(false, true, 0, false)]  // an isolated user folder (probes, captures)
    [InlineData(false, false, 1, false)] // --play, --shellshots, any flag after --
    public void OnlyAPlainWindowedLaunch_IsAttended(bool headless, bool userDir, int args, bool attended) =>
        Assert.Equal(attended, ShellFrontRules.Attended(headless, userDir, args));

    [Fact]
    public void FirstRun_IsAFreshSettingsFileAndNoSaves()
    {
        const double Started = 1_000_000.4d;
        Assert.True(ShellFrontRules.FirstRun(true, 0, false, 0d, Started));           // no file at all
        Assert.True(ShellFrontRules.FirstRun(true, 0, true, 1_000_001d, Started));    // written this boot
        Assert.True(ShellFrontRules.FirstRun(true, 0, true, 1_000_000d, Started));    // same second (whole-second mtime)
        Assert.False(ShellFrontRules.FirstRun(true, 0, true, 999_000d, Started));     // an older install
        Assert.False(ShellFrontRules.FirstRun(true, 1, true, 1_000_001d, Started));   // saves exist
        Assert.False(ShellFrontRules.FirstRun(false, 0, false, 0d, Started));         // nobody at the machine
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("{\"flags\":[\"flag.met_kael\"]}", 1)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.1\"]}", 1)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.1\",\"flag.chapter.ch.2.frostfang\"]}", 2)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.3\",\"flag.chapter.ch.2.ashen\",\"flag.chapter.ch.1\"]}", 3)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.4\"]}", 4)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.9\"]}", 4)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.1\",\"flag.game_complete\"]}", 4)]
    [InlineData("{\"flags\":[\"flag.chapter.ch.2", 1)] // a file cut off mid-flag
    public void TheAct_IsTheFurthestChapterTheSaveHolds(string? save, int act) =>
        Assert.Equal(act, ShellFrontRules.ActFromSaveText(save));

    [Fact]
    public void EachAct_HasAPainting_AndAnythingElseFallsBackToTheFirst()
    {
        Assert.Equal(UiTheme.GenericPainting, ShellFrontRules.TitlePainting(1));
        Assert.Equal("title_act2", ShellFrontRules.TitlePainting(2));
        Assert.Equal("title_act3", ShellFrontRules.TitlePainting(3));
        Assert.Equal("title_act4", ShellFrontRules.TitlePainting(4));
        Assert.Equal(UiTheme.GenericPainting, ShellFrontRules.TitlePainting(0));
        Assert.Equal(UiTheme.GenericPainting, ShellFrontRules.TitlePainting(5));
    }

    [Theory]
    [InlineData(0d, 0, 0)]
    [InlineData(-5d, 0, 0)]
    [InlineData(59d, 0, 0)]
    [InlineData(3_660d, 1, 1)]
    [InlineData(90_000d, 25, 0)]
    public void Playtime_IsWholeHoursAndMinutes(double seconds, int hours, int minutes) =>
        Assert.Equal((hours, minutes), ShellFrontRules.Playtime(seconds));

    [Fact]
    public void TheCredits_ClimbOnTheirOwn_FasterWhileHeld()
    {
        float plain = ShellFrontRules.CreditsAdvance(100f, 1f, auto: true, fast: false, 0f, 100f, 5000f);
        float fast = ShellFrontRules.CreditsAdvance(100f, 1f, auto: true, fast: true, 0f, 100f, 5000f);
        Assert.Equal(100f + ShellFrontRules.CreditsSpeed, plain);
        Assert.True(fast > plain);
    }

    [Fact]
    public void WithoutMotion_TheCreditsOnlyMoveByHand()
    {
        Assert.Equal(300f, ShellFrontRules.CreditsAdvance(300f, 1f, auto: false, fast: true, 0f, 100f, 5000f));
        Assert.True(ShellFrontRules.CreditsAdvance(300f, 1f, auto: false, fast: false, 1f, 100f, 5000f) > 300f);
        Assert.True(ShellFrontRules.CreditsAdvance(300f, 1f, auto: false, fast: false, -1f, 100f, 5000f) < 300f);
    }

    [Fact]
    public void TheCredits_NeverLeaveTheirEnds()
    {
        Assert.Equal(100f, ShellFrontRules.CreditsAdvance(110f, 10f, auto: false, fast: false, -1f, 100f, 5000f));
        Assert.Equal(5000f, ShellFrontRules.CreditsAdvance(4990f, 10f, auto: true, fast: true, 1f, 100f, 5000f));
        Assert.True(ShellFrontRules.CreditsEnd(800f, 533f) > ShellFrontRules.CreditsStart(533f));
    }
}
