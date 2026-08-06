using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.Economy;

/// <summary>Raised when the player opens a shop. Carries the resolved <see cref="ShopResource"/> so
/// the UI never has to look it up — the same "publish what you resolved" shape
/// <c>StorageOpenedEvent</c> and <c>CraftingStationOpenedEvent</c> use.</summary>
public readonly record struct ShopOpenedEvent(IEntity Player, ShopResource Shop) : IGameEvent;

/// <summary>Raised when the shop window closes.</summary>
public readonly record struct ShopClosedEvent(IEntity Player) : IGameEvent;

/// <summary>
/// A merchant the player can trade with (Phase 38A). Authored on an entity with a collider (the
/// interact raycast needs one) and pointed at a <see cref="ShopResource"/> by id — the same
/// "declare it in the scene, resolve it at runtime" shape <c>PropertyStorageComponent</c> and
/// <c>TravelNodeComponent</c> use.
///
/// ⚠️ <b>An entity gets one interactable.</b> <c>EntityNode.GetComponent&lt;T&gt;</c> returns the
/// <em>first</em> child match, so a vendor component sitting behind a <c>DialogueComponent</c> on the
/// same actor never fires. That is why the three Ember Crown stub vendors are untouched here:
/// whether trade replaces their conversation or hangs off a dialogue choice is Phase 38E's call.
/// Until then a shop is opened with <c>shop &lt;id&gt;</c> in the F1 console.
///
/// ⚠️ <b><c>ContentValidator</c> does not scan <c>.tscn</c> files</b>, so a mistyped
/// <see cref="ShopId"/> yields <em>no prompt at all</em> rather than an error — the same trap
/// <c>PropertyStorageComponent.PropertyId</c> carries. If a merchant is silently unusable in game,
/// check this field first.
/// </summary>
[GlobalClass]
public partial class VendorComponent : InteractableComponent
{
    /// <summary>Which <see cref="ShopResource"/> this merchant trades from (a <c>shop.*</c> id).</summary>
    [Export] public string ShopId { get; set; } = string.Empty;

    public override string Prompt =>
        // Nothing sensible to say about an id that resolves to nothing; the validator and the log
        // carry authoring faults, the prompt stays silent rather than lying.
        ShopDatabase.Get(ShopId) is { } shop
            ? Loc.TF("shop.prompt_trade", Loc.T(shop.NameKey))
            : string.Empty;

    public override void Interact(IEntity instigator)
    {
        if (ShopDatabase.Get(ShopId) is not { } shop)
        {
            return; // the prompt has already said nothing, for the same reason
        }

        EventBus.Instance?.Publish(new ShopOpenedEvent(instigator, shop));
    }
}
