using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Narrative;

namespace Embervale.UI;

/// <summary>
/// The player-facing Pale Concord reveal. <see cref="HiddenRealmReveal"/> publishes
/// <see cref="StoryBeatEvent"/> <c>pale_reveal</c> once, live, when the third Act II Flamebearer falls;
/// this plays a single story card (<c>pale.reveal.1</c>), then asks for a toast and for the chapter
/// banner. One card is about six seconds, skippable like every narration sequence.
///
/// The beat never talks over anything: it waits in a queue until the screen has been free for
/// <see cref="StoryBeatGate.GraceSeconds"/> (a playing vision, the absorb conversation, the opening or the
/// ending all hold <c>UiState</c>), and a load drops it, because a load restores state and never
/// narrates it.
/// </summary>
public partial class PaleRevealSequence : NarrationSequence
{
    public const string CardKey = "pale.reveal.1";
    public const string ToastKey = "pale.reveal.toast";
    public const string ToastDetailKey = "pale.reveal.toast_detail";

    /// <summary>The chapter whose banner follows the card (the first Pale Concord quest's chapter).</summary>
    public const string ChapterKey = "ch.2.pale";

    private bool _pending;
    private double _freeSeconds;

    /// <summary>The beat is waiting for a free screen. For harness validation.</summary>
    public bool IsPending => _pending;

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<StoryBeatEvent>(OnBeat);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnLoading);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryBeatEvent>(OnBeat);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnLoading);
    }

    private void OnBeat(StoryBeatEvent e)
    {
        if (e.Key == HiddenRealmReveal.RevealBeat)
        {
            _pending = true;
            _freeSeconds = 0.0;
        }
    }

    private void OnLoading(GameLoadingEvent e) => _pending = false;

    public override void _Process(double delta)
    {
        if (_pending)
        {
            bool free = GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen;
            _freeSeconds = StoryBeatGate.Advance(free, _freeSeconds, delta);
            if (StoryBeatGate.Ready(_pending, IsPlaying, free, _freeSeconds))
            {
                _pending = false;
                PlayCards(new[] { CardKey }, string.Empty);
            }
        }

        base._Process(delta);
    }

    protected override void OnSequenceFinished()
    {
        EventBus.Instance?.Publish(new StoryToastRequestedEvent(ToastKey, ToastDetailKey));
        EventBus.Instance?.Publish(new StoryBannerRequestedEvent(ChapterKey));
    }
}
