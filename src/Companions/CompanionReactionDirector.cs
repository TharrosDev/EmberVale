using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Narrative;
using Embervale.Player;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Plays the companions' one-line reactions to story beats (<c>data/story/reactions/*.json</c>). When a
/// story flag becomes set, every reaction naming it whose companion is in the party right now publishes a
/// <see cref="CompanionBarkEvent"/> (the toast), nudges that companion's loyalty and sets
/// <c>flag.bark.&lt;slug&gt;</c>, so each line plays at most once per save. A companion who is not in the
/// party misses the line: reactions are made of the moment, so nothing replays when they join later.
///
/// <para>A load restores state, it does not narrate one: flags set during a load (the quest log's
/// catch-up included) never bark. The window opens on <see cref="GameLoadingEvent"/> and closes at the end
/// of the frame the load finished in.</para>
/// </summary>
public partial class CompanionReactionDirector : Node
{
    private List<CompanionReaction> _reactions = new();
    private bool _loadWindow;

    public IReadOnlyList<CompanionReaction> Reactions => _reactions;

    public override void _Ready()
    {
        var errors = new List<string>();
        _reactions = StoryDataFiles.LoadReactions(errors);
        foreach (string error in errors)
        {
            Log.Warn($"Companion reactions: {error}");
        }

        Log.Info($"Companion reactions: {_reactions.Count} loaded.");
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnLoading);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnLoading);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnLoaded);
    }

    private void OnLoading(GameLoadingEvent e) => OpenLoadWindow();

    private void OnLoaded(GameLoadedEvent e) => OpenLoadWindow();

    private void OpenLoadWindow()
    {
        _loadWindow = true;
        CallDeferred(nameof(CloseLoadWindow));
    }

    private void CloseLoadWindow() => _loadWindow = false;

    private void OnFlag(StoryFlagChangedEvent e)
    {
        if (!e.Value || _loadWindow || _reactions.Count == 0 ||
            ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player) ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags ||
            !sl.TryGet(out CompanionRoster roster))
        {
            return;
        }

        foreach (CompanionReaction reaction in CompanionReactionData.Due(
                     _reactions, e.Flag, roster.IsRecruited, flags.Has))
        {
            // Recorded first, so a flag change raised by the bark's own consequences cannot replay it.
            flags.Set(reaction.BarkFlag);
            if (reaction.LoyaltyDelta != 0)
            {
                roster.AddLoyalty(reaction.Companion, reaction.LoyaltyDelta);
            }

            EventBus.Instance?.Publish(new CompanionBarkEvent(reaction.Companion, reaction.TextKey));
        }
    }
}
