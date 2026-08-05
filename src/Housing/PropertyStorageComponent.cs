using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.Housing;

/// <summary>Raised when the player opens a holding's storage. Carries the container's own
/// <see cref="InventoryComponent"/> so the UI never has to walk the scene to find it — the same
/// "publish what you resolved" shape <c>CraftingStationOpenedEvent</c> uses.
///
/// <paramref name="MinRarity"/> (37D) is the floor the container accepts, honoured by
/// <c>StoragePanel</c> on the <b>Store</b> direction only. A chest takes anything and leaves it at
/// the default; a display stand asks for <see cref="TrophyDisplay.MinimumRarity"/>. Taking is never
/// gated — a container that could trap an item is worse than one holding something dull.</summary>
public readonly record struct StorageOpenedEvent(
    IEntity Player,
    InventoryComponent Storage,
    string StorageName,
    ItemRarity MinRarity = ItemRarity.Common) : IGameEvent;

/// <summary>Raised when the storage window closes.</summary>
public readonly record struct StorageClosedEvent(IEntity Player) : IGameEvent;

/// <summary>
/// The stash inside a holding you own (Phase 37B). Authored on an entity that also carries an
/// <see cref="InventoryComponent"/> — that inventory <b>is</b> the storage, which is why 37B adds no
/// persistence code at all: an entity with a stable <c>PersistentId</c> already round-trips its
/// inventory through <c>SaveManager</c> (<c>inventory:&lt;PersistentId&gt;</c>) and survives region-cell
/// churn through <c>CellPersistenceDirector</c>.
///
/// <b>Capacity is authored on that inventory node, not on the property resource.</b> Deliberate:
/// <see cref="InventoryComponent.Load"/> restores through <c>AddInstance</c>, which clamps to
/// <c>Capacity</c> — a capacity written by a sibling component after the save manager's mid-load
/// restore would silently drop the overflow, and a stash that quietly eats items on reload is the
/// worst bug this feature could have.
///
/// Like <see cref="PropertyDeedComponent"/>, every refusal names itself, and the prompt and the
/// interaction read the same <see cref="PropertyStorage.Resolve"/>.
/// </summary>
[GlobalClass]
public partial class PropertyStorageComponent : InteractableComponent
{
    /// <summary>Which holding this stash belongs to (a <c>property.*</c> id).</summary>
    [Export] public string PropertyId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            PropertyResource? property = PropertyDatabase.Get(PropertyId);
            return Evaluate(property) switch
            {
                // Nothing sensible to say about an id that resolves to nothing; the validator and the
                // log carry authoring faults, the prompt stays silent rather than lying.
                StorageOutcome.UnknownProperty => string.Empty,
                StorageOutcome.NotOwned => Loc.TF("storage.prompt_locked", Loc.T(property!.NameKey)),
                _ => Loc.TF("storage.prompt_open", Loc.T(property!.NameKey)),
            };
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (PropertyDatabase.Get(PropertyId) is not { } property ||
            Evaluate(property) != StorageOutcome.Open ||
            Entity?.GetComponent<InventoryComponent>() is not { } storage)
        {
            return; // the prompt has already said why
        }

        EventBus.Instance?.Publish(
            new StorageOpenedEvent(instigator, storage, Loc.T(property.NameKey)));
    }

    private static StorageOutcome Evaluate(PropertyResource? property) => PropertyStorage.Resolve(
        propertyKnown: property != null,
        owned: property != null && (Resolve<HousingService>()?.Owns(property.Id) ?? false));

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
