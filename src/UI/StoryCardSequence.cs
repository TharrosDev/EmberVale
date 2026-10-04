using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Narrative;

namespace Embervale.UI;

/// <summary>
/// Plays the full-screen story cards a dialogue's <c>PlayCards</c> effect asks for
/// (<see cref="StoryCardsRequestedEvent"/>): the cards are <c>&lt;prefix&gt;.1</c>, <c>&lt;prefix&gt;.2</c>,
/// ... until a key is missing from the catalogue. It is always played when requested (the dialogue
/// that carries the effect decides how often it can run). A request that arrives while a sequence is
/// playing queues behind it.
/// </summary>
public partial class StoryCardSequence : NarrationSequence
{
    private readonly Queue<string[]> _queue = new();

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<StoryCardsRequestedEvent>(OnRequested);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryCardsRequestedEvent>(OnRequested);
    }

    private void OnRequested(StoryCardsRequestedEvent e)
    {
        string[] cards = StoryCards.Keys(e.Prefix, Loc.Has);
        if (cards.Length == 0)
        {
            return;
        }

        if (IsPlaying)
        {
            _queue.Enqueue(cards);
            return;
        }

        PlayCards(cards, string.Empty);
    }

    protected override void OnSequenceFinished()
    {
        if (_queue.Count > 0)
        {
            PlayCards(_queue.Dequeue(), string.Empty);
        }
    }
}
