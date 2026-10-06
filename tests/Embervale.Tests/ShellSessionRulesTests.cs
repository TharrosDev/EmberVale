using System;
using Embervale.Corruption;
using Embervale.Save;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>The session shell's pure rules: what a save row says, when the death screen may appear
/// and take input, and which painting a narration plays over.</summary>
public class ShellSessionRulesTests
{
    // --- Save rows ----------------------------------------------------------

    [Fact]
    public void EveryTierLabelASaveHeaderCanCarryReadsBackAsItsTier()
    {
        // The header stores CorruptionTiers.Label; the row must read every one of them back.
        foreach (CorruptionTier tier in Enum.GetValues<CorruptionTier>())
        {
            Assert.Equal(tier, ShellSessionRules.TierOf(CorruptionTiers.Label(tier)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("99")]
    [InlineData("-1")]
    public void AHeaderWithNoRecognisableTierReadsAsUntainted(string? label) =>
        Assert.Equal(CorruptionTier.Untainted, ShellSessionRules.TierOf(label));

    [Fact]
    public void TheTierIsReadWhateverItsCase()
    {
        Assert.Equal(CorruptionTier.Marked, ShellSessionRules.TierOf("marked"));
        Assert.Equal(CorruptionTier.Untainted, ShellSessionRules.TierOf("UNTAINTED"));
    }

    [Fact]
    public void CorruptionPipsClimbOnePerTierAndFillAtTheLast()
    {
        Assert.Equal(0, ShellSessionRules.CorruptionPips(CorruptionTier.Untainted));
        Assert.Equal(1, ShellSessionRules.CorruptionPips(CorruptionTier.Touched));
        Assert.Equal(ShellSessionRules.CorruptionPipCount, ShellSessionRules.CorruptionPips(CorruptionTier.Embers));

        // One pip per tier above untainted: a new tier would need a new pip, not a clamped one.
        Assert.Equal(ShellSessionRules.CorruptionPipCount, Enum.GetValues<CorruptionTier>().Length - 1);
    }

    [Theory]
    [InlineData(0d, 0, 0)]
    [InlineData(59d, 0, 0)]
    [InlineData(3_660d, 1, 1)]
    [InlineData(36_000d + 1_799d, 10, 29)]
    [InlineData(-5d, 0, 0)]
    [InlineData(double.NaN, 0, 0)]
    public void PlaytimeIsWholeHoursAndTheMinutesLeftOver(double seconds, int hours, int minutes) =>
        Assert.Equal((hours, minutes), ShellSessionRules.Playtime(seconds));

    [Fact]
    public void ASaveIsDatedOnThePlayersClockNotUtc()
    {
        // 2026-10-06 23:30 UTC is already the 7th two hours east of it.
        double unix = new DateTimeOffset(2026, 10, 6, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        TimeZoneInfo east = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        TimeZoneInfo west = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");

        Assert.Equal("2026-10-06 23:30", ShellSessionRules.LocalDate(unix, TimeZoneInfo.Utc));
        Assert.Equal("2026-10-07 01:30", ShellSessionRules.LocalDate(unix, east));
        Assert.Equal("2026-10-06 18:30", ShellSessionRules.LocalDate(unix, west));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(1e30)]
    public void ADateThatCannotBeReadStillFormats(double unix) =>
        Assert.Equal(16, ShellSessionRules.LocalDate(unix, TimeZoneInfo.Utc).Length);

    [Fact]
    public void OnlyAnEmptySlotIsWrittenWithoutAHold()
    {
        Assert.False(ShellSessionRules.WriteNeedsHold(SaveHealth.Missing));
        Assert.True(ShellSessionRules.WriteNeedsHold(SaveHealth.Ok));

        // A save that cannot be loaded may still be recoverable: writing over it is not free.
        Assert.True(ShellSessionRules.WriteNeedsHold(SaveHealth.Corrupt));
        Assert.True(ShellSessionRules.WriteNeedsHold(SaveHealth.Newer));
    }

    // --- Death screen -------------------------------------------------------

    [Fact]
    public void TheDeathScreenTakesNoInputUntilItsDelayHasRun()
    {
        Assert.False(ShellSessionRules.DeathAcceptsInput(0d));
        Assert.False(ShellSessionRules.DeathAcceptsInput(ShellSessionRules.DeathInputDelaySeconds - 0.01d));
        Assert.True(ShellSessionRules.DeathAcceptsInput(ShellSessionRules.DeathInputDelaySeconds));
    }

    [Theory]
    [InlineData("headless", "", 0)]
    [InlineData("Windows", "C:/tmp/run", 0)]
    [InlineData("Windows", "", 1)]
    [InlineData("headless", "C:/tmp/run", 3)]
    public void ARunNobodyIsDrivingIsUnattended(string display, string userDir, int args) =>
        Assert.True(ShellSessionRules.Unattended(display, userDir, args));

    [Theory]
    [InlineData("Windows", "")]
    [InlineData("Windows", null)]
    [InlineData("X11", "   ")]
    public void APlainLaunchOnARealDisplayIsAttended(string display, string? userDir) =>
        Assert.False(ShellSessionRules.Unattended(display, userDir, 0));

    // --- Narration ----------------------------------------------------------

    [Fact]
    public void EachEndingPlaysOverItsOwnPainting()
    {
        // Through the real script, so a renamed ending card cannot quietly lose its painting.
        Assert.Equal(ShellSessionRules.DawnfireBackdrop, ShellSessionRules.NarrationBackdrop(EndingSequence.Script(true, 0)));
        Assert.Equal(ShellSessionRules.EmbersBackdrop, ShellSessionRules.NarrationBackdrop(EndingSequence.Script(false, 6)));
        Assert.NotEqual(ShellSessionRules.DawnfireBackdrop, ShellSessionRules.EmbersBackdrop);
    }

    [Fact]
    public void EveryOtherNarrationKeepsThePlainField()
    {
        Assert.Null(ShellSessionRules.NarrationBackdrop(OpeningSequence.CardKeys));
        Assert.Null(ShellSessionRules.NarrationBackdrop(new[] { "vision.iron_king.1" }));
        Assert.Null(ShellSessionRules.NarrationBackdrop(Array.Empty<string>()));

        // The credits alone are not an ending: only a script that opens on one is.
        Assert.Null(ShellSessionRules.NarrationBackdrop(new[] { "ending.credits.1" }));
    }

    [Fact]
    public void ThePaintingComesUpSlowlyAndLeavesWithTheLastCard()
    {
        float duration = OpeningTimeline.Duration(4);
        float fade = OpeningTimeline.FadeSeconds;

        Assert.Equal(0f, ShellSessionRules.BackdropAlpha(0f, duration, fade));
        Assert.InRange(ShellSessionRules.BackdropAlpha(ShellSessionRules.BackdropFadeInSeconds * 0.5f, duration, fade), 0.49f, 0.51f);
        Assert.Equal(1f, ShellSessionRules.BackdropAlpha(duration * 0.5f, duration, fade));
        Assert.InRange(ShellSessionRules.BackdropAlpha(duration - (fade * 0.5f), duration, fade), 0.49f, 0.51f);
        Assert.Equal(0f, ShellSessionRules.BackdropAlpha(duration, duration, fade));
    }

    [Fact]
    public void ThePaintingNeverLeavesItsRange()
    {
        float duration = OpeningTimeline.Duration(1);
        for (float t = -2f; t < duration + 2f; t += 0.37f)
        {
            Assert.InRange(ShellSessionRules.BackdropAlpha(t, duration, OpeningTimeline.FadeSeconds), 0f, 1f);
        }
    }

    [Theory]
    [InlineData(1280f, 680f)]
    [InlineData(853f, 680f)]  // the Steam Deck at UI scale 1.5: the measure still fits
    [InlineData(640f, 576f)]
    [InlineData(40f, 0f)]
    public void ACardWrapsAtItsMeasureOrWhatANarrowViewLeaves(float viewWidth, float expected) =>
        Assert.Equal(expected, ShellSessionRules.NarrationWidth(viewWidth, UiTheme.NarrationMeasure, UiTheme.SpaceXl));

    [Fact]
    public void ANarrationStillFinishesOnItsOwnWithAPaintingBehindIt()
    {
        // The painting is drawn from the timeline and never extends it: the sequence is as long as
        // its cards, which is what lets the story gate run it out with no input.
        string[] cards = EndingSequence.Script(true, 0);
        Assert.True(OpeningTimeline.At(OpeningTimeline.Duration(cards.Length), cards.Length).Finished);
        Assert.False(OpeningTimeline.At(OpeningTimeline.Duration(cards.Length) - 0.01f, cards.Length).Finished);
    }

    [Fact]
    public void ALongTooltipIsBrokenIntoLinesThatFit()
    {
        // The engine draws a tooltip as one line: a race's description ran off both sides of the screen.
        const string text = "An ancient titan-blooded race of massive strength and unmatched endurance. " +
                            "Grondar move slower than most, but little can move them.";
        string wrapped = ShellSessionRules.WrapTooltip(text);
        string[] lines = wrapped.Split('\n');

        Assert.True(lines.Length > 1);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, ShellSessionRules.TooltipLineLength));
        Assert.Equal(text, wrapped.Replace('\n', ' ')); // nothing lost, nothing added but the breaks
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Short enough.", "Short enough.")]
    public void AShortTooltipIsLeftAlone(string? text, string expected) =>
        Assert.Equal(expected, ShellSessionRules.WrapTooltip(text));

    [Fact]
    public void AWrappedTooltipKeepsItsOwnBreaksAndNeverSplitsAWord()
    {
        Assert.Equal("one two\nthree\nfour", ShellSessionRules.WrapTooltip("one two three\nfour", 8));
        Assert.Equal("a\nsupercalifragilistic\nb", ShellSessionRules.WrapTooltip("a supercalifragilistic b", 6));
    }
}
