using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Races;

namespace Embervale.UI;

/// <summary>
/// The prologue (Phase 33A): the beat between character creation and the Ember Crown. A black field
/// with narration cards fading through the premise — the dying world, the six who fell, the seventh
/// who remains — closing on the player's own name before the world is revealed underneath.
///
/// It exists so a new game <em>starts</em> instead of merely beginning: the world is already built
/// and streaming behind this screen, so when the last card fades the player is standing in the town
/// with nothing to load. It plays only on New Game, never on a load, and is skippable from the first
/// frame — a veteran must never be made to sit through it.
///
/// The card rendering, pacing and input lock live in <see cref="NarrationSequence"/>, shared with
/// the slice's closing card (33D).
/// </summary>
public partial class OpeningSequence : NarrationSequence
{
    /// <summary>The narration, as <c>Loc</c> keys. The last card is formatted with the character's
    /// name, so the prologue ends on who the player just made rather than on lore.</summary>
    private static readonly string[] Cards =
    {
        "opening.card1",
        "opening.card2",
        "opening.card3",
        "opening.card4",
        "opening.card5",
    };

    /// <summary>Starts the prologue for <paramref name="profile"/>'s character.</summary>
    public void Play(CharacterProfile profile)
    {
        string name = string.IsNullOrWhiteSpace(profile.CharacterName)
            ? Loc.T("opening.nameless")
            : profile.CharacterName;

        PlayCards(Cards, name);
    }

    protected override void OnSequenceFinished() =>
        EventBus.Instance?.Publish(new OpeningFinishedEvent());
}
