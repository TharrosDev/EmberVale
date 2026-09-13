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

    /// <summary>Where fast travel sets the player down, in the waystone body's own space: a clear,
    /// walkable spot beside the post rather than on its collider. The bake records the resulting world
    /// point in <see cref="WorldPlaceIndex"/>, so a moved waystone moves every save's landing too.</summary>
    [Export] public Vector3 LandingOffset { get; set; } = new(0f, 0f, 2.5f);

    /// <summary>The landing point for a travel node parented to <paramref name="body"/>.</summary>
    public static Vector3 LandingFor(Node3D body, Vector3 offset) => body.GlobalTransform * offset;

    public override string Prompt
    {
        get
        {
            return Resolve() is { } svc && svc.HasNode(Id)
                ? Loc.TF("travel.prompt_attuned", DisplayName)
                : Loc.TF("travel.prompt_attune", DisplayName);
        }
    }

    public override bool Interact(IEntity instigator)
    {
        if (Resolve() is not { } svc || instigator.Body is not { } playerBody)
        {
            return false;
        }

        // The authored landing beside the post, never the post's own position: landing on its collider
        // trapped the player inside it. (It used to be wherever the player happened to stand, which
        // made a landing a save-local accident rather than a property of the waystone.)
        Vector3 landing = Entity?.Body is { } body ? LandingFor(body, LandingOffset) : playerBody.GlobalPosition;
        if (svc.Discover(Id, DisplayName, RegionId, landing))
        {
            Log.Info($"Attuned to {DisplayName}.");
        }

        // Attuning again to a node already known is still a use of the waystone — the player did
        // the thing the prompt offered. Only an unresolvable service above is a failure.
        return true;
    }

    private static FastTravelService? Resolve() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out FastTravelService service) ? service : null;
}
