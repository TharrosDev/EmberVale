using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the prologue's pacing (Phase 33A). A narration sequence lives or dies on its timing, and
/// the failure modes are silent — a card that never reaches full opacity, a sequence that never
/// reports finished and leaves the player locked out of their own game — so the curve is pinned here
/// rather than eyeballed in a play session.
/// </summary>
public class OpeningTimelineTests
{
    private const int Cards = 5;

    [Fact]
    public void StartsFullyFadedOut()
    {
        OpeningFrame frame = OpeningTimeline.At(0f, Cards);

        Assert.Equal(0, frame.CardIndex);
        Assert.Equal(0f, frame.Alpha, 3);
        Assert.False(frame.Finished);
    }

    [Fact]
    public void ReachesFullOpacityDuringTheHold()
    {
        OpeningFrame frame = OpeningTimeline.At(OpeningTimeline.FadeSeconds + 0.5f, Cards);

        Assert.Equal(1f, frame.Alpha, 3);
        Assert.Equal(0, frame.CardIndex);
    }

    [Fact]
    public void AdvancesThroughEveryCardInOrder()
    {
        for (int i = 0; i < Cards; i++)
        {
            float middleOfCard = (i * OpeningTimeline.CardSeconds) + OpeningTimeline.FadeSeconds + 0.1f;
            Assert.Equal(i, OpeningTimeline.At(middleOfCard, Cards).CardIndex);
        }
    }

    [Fact]
    public void FinishesAfterTheLastCard()
    {
        Assert.True(OpeningTimeline.At(OpeningTimeline.Duration(Cards), Cards).Finished);
        Assert.True(OpeningTimeline.At(OpeningTimeline.Duration(Cards) + 10f, Cards).Finished);
    }

    [Fact]
    public void DoesNotFinishEarly()
    {
        // The crux of "the player is not locked out": the sequence must stay unfinished right up to
        // its end, and must finish immediately after.
        float justBefore = OpeningTimeline.Duration(Cards) - 0.01f;
        Assert.False(OpeningTimeline.At(justBefore, Cards).Finished);
    }

    [Fact]
    public void AlphaIsAlwaysInRange()
    {
        for (float t = 0f; t < OpeningTimeline.Duration(Cards); t += 0.05f)
        {
            float alpha = OpeningTimeline.At(t, Cards).Alpha;
            Assert.InRange(alpha, 0f, 1f);
        }
    }

    [Fact]
    public void CardIndexNeverEscapesTheDeck()
    {
        for (float t = 0f; t < OpeningTimeline.Duration(Cards); t += 0.05f)
        {
            Assert.InRange(OpeningTimeline.At(t, Cards).CardIndex, 0, Cards - 1);
        }
    }

    [Fact]
    public void EmptyDeckFinishesImmediately()
    {
        // No cards must never mean an eternal black screen.
        Assert.True(OpeningTimeline.At(0f, 0).Finished);
        Assert.Equal(0f, OpeningTimeline.Duration(0));
    }

    [Fact]
    public void NegativeTimeIsTreatedAsTheStart()
    {
        OpeningFrame frame = OpeningTimeline.At(-5f, Cards);

        Assert.False(frame.Finished);
        Assert.Equal(0, frame.CardIndex);
    }
}
