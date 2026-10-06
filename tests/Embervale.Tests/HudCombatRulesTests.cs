using System.Collections.Generic;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind the combat HUD and the notices: which blows get a number, which enemies
/// keep a plate, how long a toast is read for and when it waits, and how a spoken line is cut into
/// captions.
/// </summary>
public class HudCombatRulesTests
{
    // --- Damage numbers ------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Off_ShowsNoNumber(bool byPlayer, bool onPlayer)
    {
        Assert.False(DamageNumberRules.Shows(DamageNumberRules.Off, byPlayer, onPlayer, critical: true, kill: true));
    }

    [Fact]
    public void All_ShowsBothDirections()
    {
        Assert.True(DamageNumberRules.Shows(DamageNumberRules.All, true, false, false, false));
        Assert.True(DamageNumberRules.Shows(DamageNumberRules.All, false, true, false, false));
        Assert.False(DamageNumberRules.Shows(DamageNumberRules.All, false, false, true, true));
    }

    [Fact]
    public void OwnBlows_DropsWhatThePlayerTakes()
    {
        Assert.True(DamageNumberRules.Shows(DamageNumberRules.OwnBlows, true, false, false, false));
        Assert.False(DamageNumberRules.Shows(DamageNumberRules.OwnBlows, false, true, true, false));
    }

    [Fact]
    public void CritsAndKills_KeepsOnlyThePlayersCritsAndKills()
    {
        const int mode = DamageNumberRules.CritsAndKills;
        Assert.False(DamageNumberRules.Shows(mode, true, false, critical: false, kill: false));
        Assert.True(DamageNumberRules.Shows(mode, true, false, critical: true, kill: false));
        Assert.True(DamageNumberRules.Shows(mode, true, false, critical: false, kill: true));
        Assert.False(DamageNumberRules.Shows(mode, false, true, critical: true, kill: false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void AnUnknownMode_ShowsNothing(int mode)
    {
        Assert.False(DamageNumberRules.Shows(mode, true, true, true, true));
    }

    [Fact]
    public void TheModeNumbers_AreTheOnesSettingsSaves()
    {
        // Settings.DamageNumberMode documents 0 none, 1 all, 2 own blows, 3 crits and kills.
        Assert.Equal(0, DamageNumberRules.Off);
        Assert.Equal(1, DamageNumberRules.All);
        Assert.Equal(2, DamageNumberRules.OwnBlows);
        Assert.Equal(3, DamageNumberRules.CritsAndKills);
        Assert.Equal(DamageNumberRules.All, Embervale.Settings.SettingsMath.DamageNumberMode(-1, legacyEnabled: true));
        Assert.Equal(DamageNumberRules.Off, Embervale.Settings.SettingsMath.DamageNumberMode(-1, legacyEnabled: false));
    }

    // --- Enemy plates --------------------------------------------------------------------------

    [Fact]
    public void APlate_LivesWhileItsEnemyIsRelevant()
    {
        Assert.True(EnemyPlateRules.Live(now: 100, touchedAt: 99, aggro: false, locked: false));
        Assert.False(EnemyPlateRules.Live(100, 100 - EnemyPlateRules.LingerSeconds - 0.1, false, false));
        Assert.True(EnemyPlateRules.Live(100, double.NegativeInfinity, aggro: true, locked: false));
        Assert.True(EnemyPlateRules.Live(100, double.NegativeInfinity, aggro: false, locked: true));
    }

    [Fact]
    public void OnlyHidden_RemovesThePlates()
    {
        Assert.True(EnemyPlateRules.Shows(HudElementMode.Always));
        Assert.True(EnemyPlateRules.Shows(HudElementMode.Dynamic));
        Assert.False(EnemyPlateRules.Shows(HudElementMode.Hidden));
    }

    [Fact]
    public void ANewEnemy_TakesAFreeSlotFirst()
    {
        var slots = new PlateClaim[]
        {
            new(true, false, false, 5), new(false, false, false, 0), new(true, false, false, 1),
        };
        Assert.Equal(1, EnemyPlateRules.SlotFor(slots));
    }

    [Fact]
    public void AFullPool_GivesUpTheStalestIdlePlate_ThenAggro_ThenTheLockedTarget()
    {
        var slots = new PlateClaim[]
        {
            new(true, Aggro: true, Locked: false, 1),
            new(true, Aggro: false, Locked: false, 9),
            new(true, Aggro: false, Locked: false, 4),
            new(true, Aggro: false, Locked: true, 0),
        };
        Assert.Equal(2, EnemyPlateRules.SlotFor(slots));

        slots[1] = slots[1] with { Aggro = true };
        slots[2] = slots[2] with { Aggro = true };
        Assert.Equal(0, EnemyPlateRules.SlotFor(slots));

        Assert.Equal(0, EnemyPlateRules.SlotFor(new PlateClaim[] { new(true, true, true, 3) }));
    }

    [Fact]
    public void APlate_FadesOutOverTheLastStretch()
    {
        Assert.Equal(1f, EnemyPlateRules.Alpha(0f));
        Assert.Equal(1f, EnemyPlateRules.Alpha(EnemyPlateRules.FadeDistance));
        Assert.InRange(EnemyPlateRules.Alpha((EnemyPlateRules.FadeDistance + EnemyPlateRules.MaxDistance) / 2f), 0.4f, 0.6f);
        Assert.Equal(0f, EnemyPlateRules.Alpha(EnemyPlateRules.MaxDistance));
        Assert.Equal(0f, EnemyPlateRules.Alpha(float.NaN));
    }

    // --- Toasts --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Saved", 1)]
    [InlineData("  New objective:\tfind  the ford\n", 5)]
    public void Words_AreRunsOfNonSpace(string? text, int expected)
    {
        Assert.Equal(expected, ToastRules.Words(text));
    }

    [Fact]
    public void AShortToast_HoldsForTheFloor()
    {
        Assert.Equal(3.0, ToastRules.Dwell(1, 1f), 6);
        Assert.Equal(3.0, ToastRules.Dwell(12, 1f), 6);
    }

    [Fact]
    public void ALongToast_HoldsAQuarterSecondAWord()
    {
        Assert.Equal(5.0, ToastRules.Dwell(20, 1f), 6);
        Assert.Equal(4.0, ToastRules.Dwell("one two three four five six seven eight", "nine ten eleven twelve thirteen fourteen fifteen sixteen", 1f), 6);
    }

    [Theory]
    [InlineData(0.5f, 1.5)]
    [InlineData(2f, 6.0)]
    [InlineData(3f, 9.0)]
    [InlineData(10f, 9.0)]      // clamped to the setting's range
    [InlineData(float.NaN, 3.0)] // a broken setting reads as 1
    public void TheSetting_MultipliesTheDwell(float multiplier, double expected)
    {
        Assert.Equal(expected, ToastRules.Dwell(4, multiplier), 6);
    }

    [Fact]
    public void OnlyNonCriticalToasts_WaitOutAFight()
    {
        Assert.True(ToastRules.Deferred(critical: false, inCombat: true));
        Assert.False(ToastRules.Deferred(critical: true, inCombat: true));
        Assert.False(ToastRules.Deferred(critical: false, inCombat: false));
    }

    [Fact]
    public void InAFight_TheQueueHandsOverOnlyWarnings_AndTheRestInOrderAfter()
    {
        var queue = new ToastQueue<string>();
        queue.Enqueue("pickup", critical: false);
        queue.Enqueue("level up", critical: false);
        queue.Enqueue("pack full", critical: true);

        Assert.True(queue.HasPresentable(inCombat: true));
        Assert.True(queue.TryTake(inCombat: true, out string taken));
        Assert.Equal("pack full", taken);
        Assert.False(queue.HasPresentable(inCombat: true));
        Assert.False(queue.TryTake(inCombat: true, out _));
        Assert.Equal(2, queue.Count);

        Assert.True(queue.TryTake(inCombat: false, out taken));
        Assert.Equal("pickup", taken);
        Assert.True(queue.TryTake(inCombat: false, out taken));
        Assert.Equal("level up", taken);
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryTake(inCombat: false, out _));
    }

    // --- Subtitles -----------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void NoText_IsNoPages(string? text)
    {
        Assert.Empty(SubtitleRules.Pages(text));
    }

    [Fact]
    public void AShortLine_IsOnePageOfOneLine()
    {
        List<string> pages = SubtitleRules.Pages("Sit, little ember.");
        Assert.Equal(new[] { "Sit, little ember." }, pages);
    }

    [Fact]
    public void ALine_WrapsAtAboutFortyCharacters_TwoLinesToAPage()
    {
        const string line =
            "Four hundred years I have waited for someone who was not afraid of the thunder, and now you stand here.";
        List<string> pages = SubtitleRules.Pages(line);

        Assert.Equal(2, pages.Count);
        var words = new List<string>();
        foreach (string page in pages)
        {
            string[] lines = page.Split('\n');
            Assert.InRange(lines.Length, 1, SubtitleRules.MaxLines);
            foreach (string l in lines)
            {
                Assert.True(l.Length <= SubtitleRules.LineLength, l);
                words.AddRange(l.Split(' '));
            }
        }

        // Nothing is lost, split or reordered.
        Assert.Equal(line.Split(' '), words);
    }

    [Fact]
    public void AWordLongerThanALine_TakesALineToItself()
    {
        List<string> pages = SubtitleRules.Pages("a Llanfairpwllgwyngyll b", lineLength: 10, maxLines: 2);
        Assert.Equal(new[] { "a\nLlanfairpwllgwyngyll", "b" }, pages);
    }

    [Fact]
    public void APage_GetsItsShareOfTheTime_AndNeverLessThanTheFloor()
    {
        Assert.Equal(4.0, SubtitleRules.PageSeconds(8.0, 40, 80), 6);
        Assert.Equal(SubtitleRules.MinPageSeconds, SubtitleRules.PageSeconds(2.0, 10, 80), 6);
        Assert.Equal(5.0, SubtitleRules.PageSeconds(5.0, 0, 0), 6);
    }

    [Fact]
    public void ALineWithNoDuration_GetsItsReadingTime()
    {
        Assert.Equal(SubtitleRules.MinSeconds, SubtitleRules.Seconds("Sit."), 6);
        Assert.Equal(8.0, SubtitleRules.Seconds(string.Join(' ', Twenty())), 6);
    }

    [Theory]
    [InlineData(0, UiTheme.BodyFontSize, UiTheme.CaptionFontSize)]
    [InlineData(1, UiTheme.HeaderFontSize, UiTheme.BodyFontSize)]
    [InlineData(2, UiTheme.TitleFontSize, UiTheme.HeaderFontSize)]
    [InlineData(-3, UiTheme.BodyFontSize, UiTheme.CaptionFontSize)]
    [InlineData(9, UiTheme.TitleFontSize, UiTheme.HeaderFontSize)]
    public void TheSizeSetting_PicksTypeScaleTokens_TheSpeakerOneStepUnderTheLine(int size, int line, int speaker)
    {
        Assert.Equal(line, SubtitleRules.FontToken(size));
        Assert.Equal(speaker, SubtitleRules.SpeakerFontToken(size));
        Assert.True(speaker >= UiTheme.CaptionFontSize); // the 12 px floor
    }

    private static string[] Twenty()
    {
        var words = new string[20];
        System.Array.Fill(words, "word");
        return words;
    }
}
