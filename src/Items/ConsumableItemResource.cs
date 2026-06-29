using Godot;

namespace Embervale.Items;

/// <summary>
/// An <see cref="ItemResource"/> that can be used from the backpack to apply an instant effect,
/// then is consumed (one removed from the stack). Kept to flat effect fields — like
/// <see cref="EquippableItemResource"/>'s flat bonuses — so the <c>.tres</c> stays authorable without
/// sub-resources. <see cref="InventoryComponent.Consume"/> applies it.
/// </summary>
[GlobalClass]
public partial class ConsumableItemResource : ItemResource
{
    /// <summary>Health restored on use. Zero for consumables with no healing component.</summary>
    [Export] public float HealAmount { get; set; }
}
