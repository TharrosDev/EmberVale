using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.World;

/// <summary>
/// An interactable that triggers a hard region-to-region load (Phase 25C). On the player's
/// <c>E</c> raycast it publishes a <see cref="RegionTransitionRequestedEvent"/> for
/// <see cref="TargetRegionId"/>; the bootstrap performs the swap (unload current cells,
/// re-target the streamer, teleport the player to the new region's spawn, loading screen).
/// Mirrors <see cref="Embervale.Dialogue.DialogueComponent"/>: a trigger only publishes intent.
/// </summary>
[GlobalClass]
public partial class RegionTransitionComponent : InteractableComponent
{
    /// <summary>Destination region id (a <c>region.*</c> key), resolved through the
    /// <see cref="RegionDatabase"/>.</summary>
    [Export] public string TargetRegionId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            string where = RegionDatabase.Get(TargetRegionId)?.DisplayName ?? "elsewhere";
            return Loc.TF("region.travel_prompt", where);
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (RegionDatabase.Get(TargetRegionId) == null)
        {
            Log.Warn($"RegionTransitionComponent: unknown region id '{TargetRegionId}'.");
            return;
        }

        EventBus.Instance?.Publish(new RegionTransitionRequestedEvent(TargetRegionId));
    }
}
