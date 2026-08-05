using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// The deed that sells a holding (Phase 37A). Authored on an entity in a region cell and pointed at
/// a <see cref="PropertyResource"/> by id — the same "declare it in the scene, resolve it at
/// runtime" shape <see cref="World.TravelNodeComponent"/> and <c>BossSummonComponent</c> use.
///
/// <b>Every refusal says which refusal it is.</b> That is not politeness: <c>BossSummonComponent</c>
/// learned it the hard way, and its comment is explicit that an inert interactable giving no reason
/// "reads as a bug rather than a gate". A deed you cannot afford and a deed a quest is holding shut
/// look identical from the outside, so the prompt names the one that applies.
///
/// Claiming registers the holding as a fast-travel destination, which is what makes owning it worth
/// anything before 37B's storage arrives.
/// </summary>
[GlobalClass]
public partial class PropertyDeedComponent : InteractableComponent
{
    /// <summary>Which <see cref="PropertyResource"/> this deed sells (a <c>property.*</c> id).</summary>
    [Export] public string PropertyId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            if (PropertyDatabase.Get(PropertyId) is not { } property)
            {
                return string.Empty;
            }

            string name = Loc.T(property.NameKey);
            return Evaluate(property) switch
            {
                ClaimOutcome.AlreadyOwned => Loc.TF("property.prompt_owned", name),
                ClaimOutcome.QuestLocked => Loc.TF("property.prompt_locked", name),
                ClaimOutcome.TooExpensive => Loc.TF(
                    "property.prompt_price", name, property.PriceGold, GoldHeld()),
                _ => Loc.TF("property.prompt_claim", name, property.PriceGold),
            };
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (PropertyDatabase.Get(PropertyId) is not { } property ||
            Evaluate(property) != ClaimOutcome.Granted ||
            Player() is not { } player ||
            Resolve<HousingService>() is not { } housing)
        {
            return; // the prompt has already said why
        }

        int price = PropertyClaim.PriceToCharge(property.PriceGold);
        if (price > 0)
        {
            // Both halves are a refusal, and they have to be: chained into one condition, an
            // unresolvable pack made the whole test false and fell *through* to the claim, handing
            // over a priced holding for nothing. A price that cannot be taken is a sale that does
            // not happen — the same fail-closed call QuestDone below makes.
            if (player.GetComponent<InventoryComponent>() is not { } inventory ||
                !inventory.RemoveItem(GameIds.Currency.Gold, price))
            {
                return; // the gold went somewhere between the prompt and the press; charge nothing
            }
        }

        if (!housing.Claim(property.Id))
        {
            return; // already held — never charge twice
        }

        DiscoverTravelNode(property, instigator);
        Log.Info($"Claimed {Loc.T(property.NameKey)} for {price} gold.");
    }

    /// <summary>Registers the holding as somewhere the player can return to.</summary>
    private static void DiscoverTravelNode(PropertyResource property, IEntity instigator)
    {
        if (string.IsNullOrEmpty(property.TravelNodeId) ||
            Resolve<FastTravelService>() is not { } travel ||
            instigator.Body is not { } playerBody)
        {
            return;
        }

        // The PLAYER's position, not the deed's — fast travel lands the player on this point, and
        // TravelNodeComponent already paid for landing someone inside a post's own collider.
        travel.Discover(
            property.TravelNodeId, Loc.T(property.NameKey), property.RegionId, playerBody.GlobalPosition);
    }

    private ClaimOutcome Evaluate(PropertyResource property) => PropertyClaim.Resolve(
        owned: Resolve<HousingService>()?.Owns(property.Id) ?? false,
        questDone: QuestDone(property.RequiredQuestId),
        goldHeld: GoldHeld(),
        priceGold: property.PriceGold);

    /// <summary>True when no quest is required, or the required one is complete. Fails <b>closed</b>
    /// on a missing player or log — better a deed that will not sell than one sold into a half-built
    /// world, which is the same call <c>BossSummonComponent.GateMet</c> makes.</summary>
    private static bool QuestDone(string questId)
    {
        if (string.IsNullOrEmpty(questId))
        {
            return true;
        }

        return Player()?.GetComponent<QuestLogComponent>() is { } log && log.IsCompleted(questId);
    }

    private static int GoldHeld() =>
        Player()?.GetComponent<InventoryComponent>()?.CountOf(GameIds.Currency.Gold) ?? 0;

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
