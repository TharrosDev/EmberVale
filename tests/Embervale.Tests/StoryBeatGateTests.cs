using System;
using System.Collections.Generic;
using Embervale.Narrative;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

public class StoryBeatGateTests
{
    [Fact]
    public void ABusyScreenResetsTheFreeRun() =>
        Assert.Equal(0.0, StoryBeatGate.Advance(screenFree: false, freeSeconds: 2.0, delta: 0.016));

    [Fact]
    public void AFreeScreenAccumulates() =>
        Assert.Equal(1.016, StoryBeatGate.Advance(screenFree: true, freeSeconds: 1.0, delta: 0.016), 6);

    [Fact]
    public void TheBeatWaitsForTheGraceThenPlaysOnce()
    {
        double free = 0.0;
        bool pending = true;
        int played = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            free = StoryBeatGate.Advance(true, free, 0.016);
            if (StoryBeatGate.Ready(pending, playing: false, screenFree: true, free))
            {
                pending = false;
                played++;
            }
        }

        Assert.Equal(1, played);
        Assert.False(StoryBeatGate.Ready(true, false, true, StoryBeatGate.GraceSeconds - 0.01));
        Assert.True(StoryBeatGate.Ready(true, false, true, StoryBeatGate.GraceSeconds));
    }

    [Fact]
    public void ItNeverPlaysOverAnotherSequenceOrWithNothingQueued()
    {
        Assert.False(StoryBeatGate.Ready(pending: true, playing: true, screenFree: true, freeSeconds: 99));
        Assert.False(StoryBeatGate.Ready(pending: true, playing: false, screenFree: false, freeSeconds: 99));
        Assert.False(StoryBeatGate.Ready(pending: false, playing: false, screenFree: true, freeSeconds: 99));
    }

    [Fact]
    public void TheGraceOutlastsTheBossDefeatSlowMotion() =>
        Assert.True(StoryBeatGate.GraceSeconds > 1.5);

    [Fact]
    public void TheRevealIsOneSkippableCardUnderTheEightSecondBudget()
    {
        Assert.Equal("pale.reveal.1", PaleRevealSequence.CardKey);
        Assert.True(OpeningTimeline.Duration(1) <= 8f);
        Assert.Equal("pale_reveal", HiddenRealmReveal.RevealBeat);
    }

    [Fact]
    public void TheRevealTextLivesUnderPaleKeys()
    {
        Dictionary<string, string> rows = StringsCsv.Rows();
        foreach (string key in new[] { PaleRevealSequence.CardKey, PaleRevealSequence.ToastKey, PaleRevealSequence.ToastDetailKey })
        {
            Assert.StartsWith("pale.", key);
            Assert.True(rows.TryGetValue(key, out string? text) && text.Length > 2, $"'{key}' has no text");
            Assert.DoesNotContain('—', text);
        }
    }
}
