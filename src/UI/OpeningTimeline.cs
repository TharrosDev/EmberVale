namespace Embervale.UI;

/// <summary>Where an opening sequence is at a given moment: which card is showing and how opaque it
/// is. <see cref="Finished"/> means the last card has faded out and the world should be revealed.</summary>
/// <param name="CardIndex">Index of the card being shown (clamped; meaningless once finished).</param>
/// <param name="Alpha">0..1 opacity of that card's text.</param>
/// <param name="Finished">True once every card has played out.</param>
public readonly record struct OpeningFrame(int CardIndex, float Alpha, bool Finished);

/// <summary>
/// The pure timing of the opening narration (Phase 33A). Cards play in order; each one fades in,
/// holds, and fades out. Keeping this Godot-free means the sequence's <em>pacing</em> — the thing a
/// prologue lives or dies on — can be tuned and tested without launching the game, and the screen
/// itself becomes a thin renderer of whatever this returns.
/// </summary>
public static class OpeningTimeline
{
    /// <summary>Seconds a card spends fading in, and again fading out.</summary>
    public const float FadeSeconds = 1.2f;

    /// <summary>Seconds a card holds at full opacity.</summary>
    public const float HoldSeconds = 3.4f;

    /// <summary>Total seconds one card occupies.</summary>
    public const float CardSeconds = (FadeSeconds * 2f) + HoldSeconds;

    /// <summary>The whole sequence's length for <paramref name="cardCount"/> cards.</summary>
    public static float Duration(int cardCount) => cardCount <= 0 ? 0f : cardCount * CardSeconds;

    /// <summary>
    /// The frame to draw at <paramref name="elapsed"/> seconds into a sequence of
    /// <paramref name="cardCount"/> cards.
    /// </summary>
    public static OpeningFrame At(float elapsed, int cardCount)
    {
        if (cardCount <= 0 || elapsed >= Duration(cardCount))
        {
            return new OpeningFrame(0, 0f, true);
        }

        if (elapsed < 0f)
        {
            elapsed = 0f;
        }

        int index = (int)(elapsed / CardSeconds);
        if (index >= cardCount)
        {
            return new OpeningFrame(cardCount - 1, 0f, true);
        }

        float within = elapsed - (index * CardSeconds);
        float alpha = within < FadeSeconds
            ? within / FadeSeconds
            : within < FadeSeconds + HoldSeconds
                ? 1f
                : 1f - ((within - FadeSeconds - HoldSeconds) / FadeSeconds);

        return new OpeningFrame(index, Clamp01(alpha), false);
    }

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}
