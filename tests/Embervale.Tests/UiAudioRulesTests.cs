using System;
using System.Collections.Generic;
using Embervale.Audio;
using Embervale.Settings;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the decisions behind the interface's sound (<c>UiAudio</c>): one sound per action, a focus
/// tick only for a step the player took, and a voice pool that never refuses a sound.
/// </summary>
public class UiAudioRulesTests
{
    [Fact]
    public void EveryCue_HasItsOwnStreamOnTheUiBus()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiCue cue in Enum.GetValues<UiCue>())
        {
            string id = UiAudioRules.CueId(cue);
            Assert.True(seen.Add(id), $"{cue} shares the stream '{id}' with another cue.");
            Assert.Equal(AudioBuses.Ui, AudioCueRouting.BusFor(id));
            Assert.False(AudioCueRouting.IsPositional(id));
        }
    }

    [Fact]
    public void ThePlainClick_IsTheCueButtonsHaveAlwaysPlayed()
    {
        Assert.Equal("ui.click", UiAudioRules.CueId(UiCue.Click));
    }

    [Fact]
    public void ACue_PlaysWhenNothingHasPlayedOrTheWindowHasPassed()
    {
        Assert.True(UiAudioRules.ShouldPlay(UiCue.Close, 10d, null, 0d));
        Assert.True(UiAudioRules.ShouldPlay(UiCue.Close, 10d, UiCue.Denied, 10d - UiAudioRules.CoalesceSeconds));
        Assert.True(UiAudioRules.ShouldPlay(UiCue.Click, 10d, UiCue.Click, 9d));
    }

    [Theory]
    [InlineData(UiCue.Click, UiCue.Open)]   // a button that opens a screen
    [InlineData(UiCue.Click, UiCue.Close)]  // a button that closes one
    [InlineData(UiCue.Back, UiCue.Close)]   // cancel closing a panel
    [InlineData(UiCue.Tab, UiCue.Close)]    // a hub step: one screen closes...
    [InlineData(UiCue.Tab, UiCue.Open)]     // ...and its neighbour opens
    [InlineData(UiCue.Click, UiCue.Click)]  // two presses in one frame
    [InlineData(UiCue.Open, UiCue.Focus)]   // a screen opening and grabbing focus
    public void InsideTheWindow_TheLesserCueYields(UiCue first, UiCue second)
    {
        Assert.False(UiAudioRules.ShouldPlay(second, 5.01d, first, 5d));
    }

    [Theory]
    [InlineData(UiCue.Click, UiCue.Denied)]
    [InlineData(UiCue.Click, UiCue.Confirm)]
    [InlineData(UiCue.Focus, UiCue.Click)]
    [InlineData(UiCue.Close, UiCue.Open)]
    public void InsideTheWindow_ACueThatMeansMoreIsStillHeard(UiCue first, UiCue second)
    {
        Assert.True(UiAudioRules.ShouldPlay(second, 5.01d, first, 5d));
    }

    [Fact]
    public void ARefusal_OutranksEverything()
    {
        foreach (UiCue cue in Enum.GetValues<UiCue>())
        {
            if (cue != UiCue.Denied)
            {
                Assert.True(UiAudioRules.Priority(UiCue.Denied) > UiAudioRules.Priority(cue));
            }
        }
    }

    [Fact]
    public void AHoldTick_IsNeverSwallowed()
    {
        Assert.True(UiAudioRules.ShouldPlay(UiCue.HoldTick, 5.001d, UiCue.Denied, 5d));
    }

    [Fact]
    public void AClockThatRanBackwards_DoesNotSilenceTheInterface()
    {
        Assert.True(UiAudioRules.ShouldPlay(UiCue.Close, 1d, UiCue.Denied, 50d));
        Assert.True(UiAudioRules.ShouldTickFocus(true, 1d, 50d));
    }

    [Fact]
    public void Focus_TicksOnlyForAStepThePlayerTook()
    {
        Assert.False(UiAudioRules.ShouldTickFocus(navigating: false, 10d, 0d));
        Assert.True(UiAudioRules.ShouldTickFocus(navigating: true, 10d, 0d));
    }

    [Fact]
    public void Focus_IsRateLimited()
    {
        double last = 10d;
        Assert.False(UiAudioRules.ShouldTickFocus(true, last + (UiAudioRules.FocusIntervalSeconds * 0.5), last));
        Assert.True(UiAudioRules.ShouldTickFocus(true, last + UiAudioRules.FocusIntervalSeconds, last));
    }

    [Fact]
    public void AHold_TicksOncePerStepOnTheWayUpAndNotOnCompletion()
    {
        int ticks = 0;
        float before = 0f;
        for (int i = 1; i <= 200; i++)
        {
            float after = i / 200f;
            if (UiAudioRules.HoldTicks(before, after))
            {
                ticks++;
            }

            before = after;
        }

        Assert.Equal(UiAudioRules.HoldSteps - 1, ticks);
        Assert.False(UiAudioRules.HoldTicks(0.6f, 0.3f)); // draining
        Assert.False(UiAudioRules.HoldTicks(0.9f, 1f));   // completion has its own cue
    }

    [Fact]
    public void AHoldTick_ClimbsInPitch()
    {
        Assert.Equal(1f, UiAudioRules.HoldPitch(0f));
        Assert.True(UiAudioRules.HoldPitch(0.3f) > UiAudioRules.HoldPitch(0.1f));
        Assert.True(UiAudioRules.HoldPitch(0.8f) > UiAudioRules.HoldPitch(0.55f));
    }

    [Fact]
    public void NextVoice_TakesTheFirstIdleVoiceAfterTheLast()
    {
        Assert.Equal(0, UiAudioRules.NextVoice(new[] { false, false, false, false }, -1));
        Assert.Equal(2, UiAudioRules.NextVoice(new[] { true, true, false, false }, 0));
        Assert.Equal(0, UiAudioRules.NextVoice(new[] { false, true, true, true }, 1));
    }

    [Fact]
    public void NextVoice_CutsTheOldestWhenAllAreBusy()
    {
        bool[] busy = { true, true, true, true };
        Assert.Equal(2, UiAudioRules.NextVoice(busy, 1));
        Assert.Equal(0, UiAudioRules.NextVoice(busy, 3));
        Assert.Equal(-1, UiAudioRules.NextVoice(ReadOnlySpan<bool>.Empty, 0));
    }
}
