using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Player;
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

    public override string Prompt
    {
        get
        {
            // Nothing sensible to say about an id that resolves to nothing; the validator and the log
            // carry authoring faults, the prompt stays silent rather than lying.
            if (ShopDatabase.Get(ShopId) is not { } shop)
            {
                return string.Empty;
            }

            string name = Loc.T(shop.NameKey);
            return WillTrade(shop)
                ? Loc.TF("shop.prompt_trade", name)
                : Loc.TF("shop.prompt_hostile", name);
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (ShopDatabase.Get(ShopId) is not { } shop || !WillTrade(shop))
        {
            return; // the prompt has already said why
        }

        EventBus.Instance?.Publish(new ShopOpenedEvent(instigator, shop));
    }

    /// <summary>
    /// Whether the merchant deals with the player at all (Phase 38C). Read by both the prompt and the
    /// interaction, so a refusal cannot say one thing and the press do another — the same rule
    /// <c>PropertyClaim.Resolve</c> enforces for a deed.
    ///
    /// Hostility is <see cref="ReputationComponent.IsHostile"/>, the game's one existing reputation
    /// verb, which already keys off each faction's authored <c>HostileThreshold</c> — the Frostfang
    /// clans tolerate someone the villagers would turn away, and that is content, not a second
    /// threshold to invent here.
    ///
    /// ⚠️ <b>The default is inverted from the AI's.</b> <c>EnemyAIComponent.PlayerIsTarget</c> treats a
    /// missing <c>ReputationComponent</c> as hostile, which is the right fail-safe for a creature
    /// deciding whether to attack. For a shop it would mean every merchant in a half-built world
    /// refusing to trade, so an unresolvable standing trades normally.
    /// </summary>
    private static bool WillTrade(ShopResource shop)
    {
        if (string.IsNullOrEmpty(shop.FactionId))
        {
            return true;
        }

        return Player()?.GetComponent<ReputationComponent>() is not { } reputation ||
            !reputation.IsHostile(shop.FactionId);
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;
}
