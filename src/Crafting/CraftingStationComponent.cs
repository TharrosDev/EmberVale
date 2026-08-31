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

    [Export] public string StationName { get; set; } = "Forge";

    public override string Prompt => $"Use {StationName}";

    public override bool Interact(IEntity instigator)
    {
        // Only actors that can craft open the station.
        if (instigator.GetComponent<CraftingComponent>() == null)
        {
            return false;
        }

        EventBus.Instance?.Publish(new CraftingStationOpenedEvent(instigator, Station, StationName));
        return true;
    }
}
