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

    /// <summary>Player-facing name of this waystone/travel point.</summary>
    [Export] public string TravelName { get; set; } = string.Empty;

    /// <summary>Region this node lives in (a <c>region.*</c> key), resolved on jump.</summary>
    [Export] public string RegionId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            string name = string.IsNullOrEmpty(TravelName) ? "waystone" : TravelName;
            return Resolve() is { } svc && svc.HasNode(Id)
                ? Loc.TF("travel.prompt_attuned", name)
                : Loc.TF("travel.prompt_attune", name);
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (Resolve() is not { } svc || Entity?.Body is not { } body)
        {
            return;
        }

        if (svc.Discover(Id, TravelName, RegionId, body.GlobalPosition))
        {
            Log.Info($"Attuned to {TravelName}.");
        }
    }

    private static FastTravelService? Resolve() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out FastTravelService service) ? service : null;
}
