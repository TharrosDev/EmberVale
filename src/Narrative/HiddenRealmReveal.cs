using System;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Narrative;

/// <summary>
/// Opens the hidden realm (finish plan, Act II): once all three Act II known-realm Flamebearers have
/// fallen, sets <see cref="RevealedFlag"/>. That flag is the hidden region's <c>UnlockFlagId</c>, so
/// the door to it appears and becomes usable with no further code, and its map records surface.
/// Evaluated on every flag change and again on load / region change so an old save that already holds
/// all three defeats catches up.
/// </summary>
public partial class HiddenRealmReveal : Node
{
    public const string RevealedFlag = "flag.pale_concord_revealed";

    /// <summary>The Act II defeats that open it. The Iron King is Act I and always done by then.</summary>
    public static readonly string[] RequiredFlags =
    {
        "flag.storm_tyrant_defeated",
        "flag.beast_lord_defeated",
        "flag.crimson_prophet_defeated",
    };

    /// <summary>Pure rule: every required defeat is held and the reveal has not happened yet.</summary>
    public static bool ShouldReveal(Func<string, bool> has) =>
        !has(RevealedFlag) && Array.TrueForAll(RequiredFlags, f => has(f));

    public override void _Ready()
    {
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnLoaded);
        EventBus.Instance?.Subscribe<RegionChangedEvent>(OnRegion);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnLoaded);
        EventBus.Instance?.Unsubscribe<RegionChangedEvent>(OnRegion);
    }

    /// <summary>The beat the live reveal announces (never on a load catch-up).</summary>
    public const string RevealBeat = "pale_reveal";

    // True from a game load until the end of that frame: a save that already holds the three defeats
    // catches up silently, however many events the load produces.
    private bool _catchUpWindow;

    private void OnFlag(StoryFlagChangedEvent e)
    {
        if (e.Value && e.Flag != RevealedFlag)
        {
            Evaluate(_catchUpWindow);
        }
    }

    private void OnLoaded(GameLoadedEvent e)
    {
        _catchUpWindow = true;
        Evaluate(silent: true);
        CallDeferred(nameof(EndCatchUp));
    }

    private void EndCatchUp() => _catchUpWindow = false;

    private void OnRegion(RegionChangedEvent e) => Evaluate(_catchUpWindow);

    private static void Evaluate(bool silent)
    {
        if (ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player) ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags || !ShouldReveal(flags.Has))
        {
            return;
        }

        flags.Set(RevealedFlag);
        if (silent)
        {
            Log.Info("Hidden realm reveal caught up silently from a load.");
            return;
        }

        Log.Info("Hidden realm revealed: all three Act II Flamebearers have fallen.");
        EventBus.Instance?.Publish(new StoryBeatEvent(RevealBeat));
    }
}
