namespace Embervale.Narrative;

/// <summary>
/// Pure rule for when a queued story beat may take the screen. A beat waits while anything else holds
/// it (a conversation, the opening, a vision, the ending: every one of them registers with
/// <c>UiState</c>) and then for a short run of free screen, so the moment between a boss falling and its
/// absorb conversation opening is not mistaken for a free screen.
/// </summary>
public static class StoryBeatGate
{
    /// <summary>Seconds the screen must stay free before a queued beat plays. Longer than the boss
    /// defeat slow-motion that precedes the absorb conversation.</summary>
    public const double GraceSeconds = 2.5;

    /// <summary>Continuous free-screen time after another frame of <paramref name="delta"/>; a busy
    /// screen resets it.</summary>
    public static double Advance(bool screenFree, double freeSeconds, double delta) =>
        screenFree ? freeSeconds + delta : 0.0;

    /// <summary>The beat is queued, nothing of its own is playing, the screen is free and has been for
    /// the grace period.</summary>
    public static bool Ready(bool pending, bool playing, bool screenFree, double freeSeconds) =>
        pending && !playing && screenFree && freeSeconds >= GraceSeconds;
}
