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

    private void OnFlag(StoryFlagChangedEvent e)
    {
        if (e.Value && e.Flag != RevealedFlag)
        {
            Evaluate();
        }
    }

    private void OnLoaded(GameLoadedEvent e) => Evaluate();

    private void OnRegion(RegionChangedEvent e) => Evaluate();

    private static void Evaluate()
    {
        if (ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player) ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags || !ShouldReveal(flags.Has))
        {
            return;
        }

        flags.Set(RevealedFlag);
        Log.Info("Hidden realm revealed: all three Act II Flamebearers have fallen.");
    }
}
