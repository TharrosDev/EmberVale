using Embervale.Core.Events;
using Embervale.Narrative;

namespace Embervale.UI;

/// <summary>
/// The slice's closing card (Phase 33D). One narration card on <see cref="SliceCompletedEvent"/>:
/// what the player did with the Iron King's ember, and what noticed. It used to run on to a third
/// card naming the next realms; missions 10 to 18 and the Elder's aftermath now say where to go, so
/// the card stops at the hook.
///
/// The card <b>branches on the choice</b> — that single branch is what makes the ending feel
/// like the game was paying attention, and it is the cheapest possible way to pay off the beat the
/// whole slice is built around.
///
/// Like the prologue it plays over live gameplay: the Frostfang region load runs underneath the
/// black, so when the cards lift the player is standing in the next region rather than looking at a
/// loading screen.
/// </summary>
public partial class ClosingSequence : NarrationSequence
{
    private static readonly string[] AbsorbedCards = { "closing.absorbed" };

    private static readonly string[] RefusedCards = { "closing.refused" };

    /// <summary>The closing card for what the player did with the Iron King's ember. Pure, so the
    /// branch is unit-testable.</summary>
    public static string[] CardsFor(bool absorbedEmber) => absorbedEmber ? AbsorbedCards : RefusedCards;

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<SliceCompletedEvent>(OnSliceCompleted);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SliceCompletedEvent>(OnSliceCompleted);
    }

    private void OnSliceCompleted(SliceCompletedEvent e) =>
        PlayCards(CardsFor(e.AbsorbedEmber), string.Empty);

    protected override void OnSequenceFinished()
    {
        // Nothing waits on the ending — the player is already standing in Frostfang. A capture build
        // stops here; Phase 44 replaces this with the real ending flow.
    }
}
