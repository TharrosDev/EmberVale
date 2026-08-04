using Godot;

namespace Embervale.Items;

/// <summary>
/// An <see cref="ItemResource"/> the player can set down in a holding they own (Phase 37C): a
/// crafting-station kit or a piece of decoration. Placing consumes one; removing the prop returns
/// one, so a kit is a prop in transit rather than a purchase you can regret.
///
/// The subclass <em>is</em> the marker — deliberately, instead of a new <c>ItemType</c> value.
/// <see cref="ItemType"/> ordinals are persisted in every save, so appending to it is a change that
/// can never be undone; a type test costs nothing and risks nothing. <c>InventoryPanel</c> filters
/// on it to offer the Place button.
/// </summary>
[GlobalClass]
public partial class PlaceableItemResource : ItemResource
{
    /// <summary>
    /// What this kit builds — a <c>prop.*</c> id registered in <see cref="Housing.PlaceableTemplates"/>
    /// and handed to <c>PersistentSpawnDirector.Spawn</c>. ⚠️ An id nothing has registered makes a
    /// kit that crafts, stacks and carries perfectly and then does nothing at all when placed, which
    /// is why <c>--validate</c> rejects one.
    /// </summary>
    [Export] public string TemplateId { get; set; } = string.Empty;
}
