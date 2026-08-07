using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.World;

/// <summary>
/// A world interactable that registers itself as a fast-travel destination (Phase 25G). On the
/// player's <c>E</c> raycast it attunes the node — recording its id, label, region and current world
/// position with the <see cref="FastTravelService"/>, which reveals it on the map screen as a
/// jump target. Mirrors <see cref="RegionTransitionComponent"/>: a placed interactable that only
/// records intent/discovery; the actual jump is driven from the map.
/// </summary>
[GlobalClass]
public partial class TravelNodeComponent : InteractableComponent
{
    /// <summary>Stable node id (a <c>travel.*</c> key).</summary>
    [Export] public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Player-facing name of this waystone/travel point. Passed through <c>Loc.T</c>, so it may be a
    /// locale key — which is what CLAUDE.md §6 asks for and what new nodes should author.
    ///
    /// ponytail: <c>Loc.T</c> on a plain string returns it unchanged, so the Phase 25 waystone's
    /// authored English still renders correctly and did not have to be migrated to close the rule.
    /// </summary>
    [Export] public string TravelName { get; set; } = string.Empty;

    /// <summary>The display name, resolved through the locale catalogue.</summary>
    private string DisplayName => string.IsNullOrEmpty(TravelName) ? "waystone" : Loc.T(TravelName);

    /// <summary>Region this node lives in (a <c>region.*</c> key), resolved on jump.</summary>
    [Export] public string RegionId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            return Resolve() is { } svc && svc.HasNode(Id)
                ? Loc.TF("travel.prompt_attuned", DisplayName)
                : Loc.TF("travel.prompt_attune", DisplayName);
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (Resolve() is not { } svc || instigator.Body is not { } playerBody)
        {
            return;
        }

        // Record where the PLAYER stands to attune — a known-walkable spot beside the post — not the
        // post's own position. Fast travel lands the player at this point; landing on the post's own
        // collider trapped them inside it.
        if (svc.Discover(Id, DisplayName, RegionId, playerBody.GlobalPosition))
        {
            Log.Info($"Attuned to {DisplayName}.");
        }
    }

    private static FastTravelService? Resolve() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out FastTravelService service) ? service : null;
}
