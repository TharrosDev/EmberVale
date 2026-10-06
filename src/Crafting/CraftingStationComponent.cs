using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Godot;

namespace Embervale.Crafting;

/// <summary>
/// A world crafting station the player uses via the <c>E</c> interact raycast. It carries
/// a <see cref="CraftingStationType"/>; interacting publishes a
/// <see cref="CraftingStationOpenedEvent"/> that the crafting UI listens for and filters
/// its recipe list by. Add a collider so the player's raycast can hit it.
/// </summary>
[GlobalClass]
public partial class CraftingStationComponent : InteractableComponent
{
    [Export] public CraftingStationType Station { get; set; } = CraftingStationType.Forge;

    /// <summary>An optional locale key naming this particular station (a master's own anvil, say).
    /// Anything that is not a key in the catalogue, including the English word older scenes authored
    /// here, is ignored and the station is named after its <see cref="Station"/> type.</summary>
    [Export] public string StationName { get; set; } = string.Empty;

    /// <summary>The station's name as the player reads it.</summary>
    public string StationLabel =>
        Localization.Loc.Has(StationName) ? Localization.Loc.T(StationName) : CraftingStations.Label(Station);

    public override string Prompt => Localization.Loc.TF("craft.prompt_use", StationLabel);

    public override bool Interact(IEntity instigator)
    {
        // Only actors that can craft open the station.
        if (instigator.GetComponent<CraftingComponent>() == null)
        {
            return false;
        }

        EventBus.Instance?.Publish(new CraftingStationOpenedEvent(instigator, Station, StationLabel));
        return true;
    }
}
