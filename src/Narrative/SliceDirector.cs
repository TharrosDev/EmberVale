using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Narrative;

/// <summary>
/// Watches for the end of the vertical slice (Phase 33D): the player stepping through the Frostfang
/// door <em>after</em> the Iron King has fallen. It raises <see cref="SliceCompletedEvent"/> once,
/// carrying whether the player took his ember, and sets <see cref="CompletedFlag"/> so a reload
/// never replays the ending.
///
/// This is deliberately tiny. It exists to give the closing card something to hang off and to give a
/// capture build a clean stopping point — it is <b>not</b> an ending system. The real endings
/// (Dawnfire vs the Lord of Embers) are Phase 44, and they key off corruption and companion loyalty,
/// not off this flag.
/// </summary>
public partial class SliceDirector : Node
{
    /// <summary>Set on the player once the slice's final beat has played.</summary>
    public const string CompletedFlag = "flag.slice_complete";

    /// <summary>Set by the Iron King's absorb dialogue when the player takes his ember.</summary>
    public const string AbsorbedFlag = "flag.iron_king_absorbed";

    public override void _Ready()
    {
        EventBus.Instance?.Subscribe<RegionChangedEvent>(OnRegionTransition);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionChangedEvent>(OnRegionTransition);
    }

    private void OnRegionTransition(RegionChangedEvent e)
    {
        if (Flags() is not { } flags || !flags.Has(BossEncounterDirector.DefeatedFlag) ||
            flags.Has(CompletedFlag))
        {
            return;
        }

        flags.Set(CompletedFlag);

        // The region load proceeds underneath: the closing cards cover it exactly the way the
        // prologue covers the world build, so when they lift the player is already standing in
        // Frostfang rather than watching a loading screen.
        EventBus.Instance?.Publish(new SliceCompletedEvent(flags.Has(AbsorbedFlag)));
        Log.Info($"Vertical slice complete (ember {(flags.Has(AbsorbedFlag) ? "taken" : "refused")}).");
    }

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;
}
