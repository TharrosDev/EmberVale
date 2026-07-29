using Embervale.Core.Events;
using Embervale.Narrative;

namespace Embervale.UI;

/// <summary>
/// The slice's closing card (Phase 33D). Three narration cards on
/// <see cref="SliceCompletedEvent"/>: what the player did with the Iron King's ember, what noticed,
/// and where the story goes next.
///
/// The first card <b>branches on the choice</b> — that single branch is what makes the ending feel
/// like the game was paying attention, and it is the cheapest possible way to pay off the beat the
/// whole slice is built around.
///
/// Like the prologue it plays over live gameplay: the Frostfang region load runs underneath the
/// black, so when the cards lift the player is standing in the next region rather than looking at a
/// loading screen.
/// </summary>
public partial class ClosingSequence : NarrationSequence
{
    private static readonly string[] AbsorbedCards =
    {
        "closing.absorbed",
        "closing.answer",
        "closing.next",
    };

    private static readonly string[] RefusedCards =
    {
        "closing.refused",
        "closing.answer",
        "closing.next",
    };

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<SliceCompletedEvent>(OnSliceCompleted);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SliceCompletedEvent>(OnSliceCompleted);
    }

    private void OnSliceCompleted(SliceCompletedEvent e) =>
        PlayCards(e.AbsorbedEmber ? AbsorbedCards : RefusedCards, string.Empty);

    protected override void OnSequenceFinished()
    {
        // Nothing waits on the ending — the player is already standing in Frostfang. A capture build
        // stops here; Phase 44 replaces this with the real ending flow.
    }
}
