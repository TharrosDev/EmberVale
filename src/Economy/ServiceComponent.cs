using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Housing;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// A paid service the player interacts with (Phase 38D) — a trainer, a bank vault, an inn bed or a
/// stablemaster. One component covers all four, branching on <see cref="ServiceResource.Kind"/> the way
/// <c>WorldEventDirector</c> branches on <c>WorldEventKind</c>: the shared half (price, standing,
/// refusal, charge) is written once and each verb is its own small method.
///
/// Every refusal names itself, and <see cref="Prompt"/> and <see cref="Interact"/> read the same
/// <see cref="ServiceRules.Resolve"/>, so what the prompt says and what the press does cannot drift —
/// the rule <c>PropertyDeedComponent</c> established for a deed.
///
/// ⚠️ <b>An entity gets one interactable.</b> <c>EntityNode.GetComponent&lt;T&gt;</c> returns the first
/// child match, so a service sitting behind a <c>DialogueComponent</c> on the same actor never fires.
/// The innkeeper's placeholder conversation is replaced by this; the three shop vendors keep theirs
/// until Phase 38E decides how trade and dialogue share an NPC.
///
/// ⚠️ <b><c>ContentValidator</c> does not scan <c>.tscn</c> files</b>, so a mistyped
/// <see cref="ServiceId"/> yields <em>no prompt at all</em> rather than an error — the same trap
/// <c>VendorComponent.ShopId</c> and <c>PropertyStorageComponent.PropertyId</c> carry.
/// </summary>
[GlobalClass]
public partial class ServiceComponent : InteractableComponent
{
    /// <summary>Which <see cref="ServiceResource"/> this offers (a <c>service.*</c> id).</summary>
    [Export] public string ServiceId { get; set; } = string.Empty;

    public override string Prompt
    {
        get
        {
            if (ServiceDatabase.Get(ServiceId) is not { } service)
            {
                return string.Empty; // authoring faults belong in the log, not in the player's face
            }

            string name = Loc.T(service.NameKey);
            int price = PriceOf(service);

            return Evaluate(service) switch
            {
                ServiceOutcome.Unknown => string.Empty,
                ServiceOutcome.Hostile => Loc.TF("service.prompt_hostile", name),
                ServiceOutcome.AlreadyHeld => Loc.TF(HeldKey(service.Kind), name),
                ServiceOutcome.CannotAfford => Loc.TF("service.prompt_price", name, price, GoldHeld()),
                _ => price > 0
                    ? Loc.TF(OfferKey(service.Kind), name, price)
                    : Loc.TF("service.prompt_free", name),
            };
        }
    }

    public override void Interact(IEntity instigator)
    {
        if (ServiceDatabase.Get(ServiceId) is not { } service ||
            Evaluate(service) != ServiceOutcome.Granted ||
            instigator.GetComponent<InventoryComponent>() is not { } pack)
        {
            return; // the prompt has already said why
        }

        // Charged before the verb, and both halves are separate conditions for the reason
        // PropertyDeedComponent spells out: chained into one test, an unresolvable pack falls *through*
        // to a free service. Unlike a purchase there is nothing to roll back — a refill, a flag and a
        // taught recipe cannot fail after the gold is taken.
        int price = PriceOf(service);
        if (price > 0 && !pack.RemoveItem(GameIds.Currency.Gold, price))
        {
            return; // the gold went somewhere between the prompt and the press; deliver nothing
        }

        switch (service.Kind)
        {
            case ServiceKind.Trainer:
                Train(service, instigator);
                break;
            case ServiceKind.Bank:
                OpenVault(service, instigator);
                break;
            case ServiceKind.Inn:
                Rest(service, instigator);
                break;
            default:
                StableMount(service, instigator);
                break;
        }

        Log.Info($"Service '{service.Id}' used for {price} gold.");
    }

    // --- the four verbs -----------------------------------------------------

    /// <summary>Teaches every recipe the player does not already know, and grants the lesson's XP.
    /// Points arrive by levelling, never by purchase — see <see cref="ServiceResource.XpReward"/>.</summary>
    private static void Train(ServiceResource service, IEntity instigator)
    {
        if (instigator.GetComponent<CraftingComponent>() is { } crafting)
        {
            foreach (string recipeId in service.TaughtRecipeIds)
            {
                crafting.Learn(recipeId); // false when already known; Train is only reached if some are not
            }
        }

        if (service.XpReward > 0)
        {
            instigator.GetComponent<ProgressionComponent>()?.AddXp(service.XpReward);
        }

        Unlock(service, instigator);
    }

    /// <summary>Opens the vault. The container's own <see cref="InventoryComponent"/> <b>is</b> the
    /// storage, exactly as in 37B — which is why this adds no persistence code and no UI: the existing
    /// <c>StoragePanel</c> already answers <c>StorageOpenedEvent</c>.</summary>
    private void OpenVault(ServiceResource service, IEntity instigator)
    {
        if (Entity?.GetComponent<InventoryComponent>() is not { } vault)
        {
            Log.Warn($"Service '{service.Id}' is a bank with no InventoryComponent on its entity.");
            return;
        }

        Unlock(service, instigator);
        EventBus.Instance?.Publish(new StorageOpenedEvent(instigator, vault, Loc.T(service.NameKey)));
    }

    /// <summary>
    /// Rests until the authored hour and refills every resource stat.
    ///
    /// ⚠️ The clock target comes from <see cref="ServiceRules.RestTarget"/>, which adds 24 for a
    /// backwards-looking hour. Passing <c>RestHour</c> straight to <c>SetTimeOfDay</c> would rewind the
    /// hour without advancing <c>Day</c> and quietly freeze 38B's restock clock.
    /// </summary>
    private static void Rest(ServiceResource service, IEntity instigator)
    {
        if (Resolve<WorldClock>() is { } clock)
        {
            clock.SetTimeOfDay(ServiceRules.RestTarget(clock.TimeOfDay, service.RestHour));
        }

        instigator.GetComponent<StatsComponent>()?.RefillResources();
    }

    /// <summary>Sells the right to a mount. The flag is the whole contract until Phase 39A's
    /// <c>MountComponent</c> reads it — real, persisted, and refusing to sell twice.</summary>
    private static void StableMount(ServiceResource service, IEntity instigator) =>
        Unlock(service, instigator);

    /// <summary>Records a one-off purchase. A service with no flag is pay-per-use and this is a no-op;
    /// the validator is what stops a one-off service being authored without one.</summary>
    private static void Unlock(ServiceResource service, IEntity instigator)
    {
        if (!string.IsNullOrEmpty(service.UnlockFlagId))
        {
            instigator.GetComponent<StoryFlagsComponent>()?.Set(service.UnlockFlagId);
        }
    }

    // --- shared evaluation --------------------------------------------------

    private ServiceOutcome Evaluate(ServiceResource? service)
    {
        if (service == null)
        {
            return ServiceOutcome.Unknown;
        }

        return ServiceRules.Resolve(
            known: true,
            hostile: IsHostileTo(service.FactionId),
            alreadyHeld: AlreadyHeld(service),
            price: PriceOf(service),
            goldHeld: GoldHeld());
    }

    /// <summary>
    /// Whether there is nothing left to buy. A one-off service asks its flag; a trainer asks whether it
    /// still has anything to teach, which is the honest equivalent — being paid twice for the same
    /// recipe is the thing to prevent, not a second visit for a different one.
    /// </summary>
    private static bool AlreadyHeld(ServiceResource service)
    {
        if (!string.IsNullOrEmpty(service.UnlockFlagId))
        {
            return Player()?.GetComponent<StoryFlagsComponent>()?.Has(service.UnlockFlagId) ?? false;
        }

        if (service.Kind != ServiceKind.Trainer)
        {
            return false; // a night's rest is always available
        }

        if (service.XpReward > 0)
        {
            return false; // XP is always worth buying again
        }

        if (Player()?.GetComponent<CraftingComponent>() is not { } crafting)
        {
            return false;
        }

        foreach (string recipeId in service.TaughtRecipeIds)
        {
            if (!crafting.Knows(recipeId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ⚠️ <b>Inverted from the AI's fail-safe, deliberately.</b> <c>EnemyAIComponent</c> treats a missing
    /// <c>ReputationComponent</c> as hostile — right for something deciding whether to swing. For a
    /// service it would make every innkeeper in a half-built world turn the player away, so an
    /// unresolvable standing serves normally. Same call <c>VendorComponent.WillTrade</c> makes.
    /// </summary>
    private static bool IsHostileTo(string factionId)
    {
        if (string.IsNullOrEmpty(factionId))
        {
            return false;
        }

        return Player()?.GetComponent<ReputationComponent>() is { } reputation &&
            reputation.IsHostile(factionId);
    }

    private static int PriceOf(ServiceResource service) =>
        ShopPricing.ServicePrice(service.PriceGold, StandingWith(service.FactionId));

    private static ReputationTier StandingWith(string factionId)
    {
        if (string.IsNullOrEmpty(factionId) ||
            Player()?.GetComponent<ReputationComponent>() is not { } reputation)
        {
            return ReputationTier.Neutral; // the no-effect tier
        }

        return reputation.TierOf(factionId);
    }

    private static int GoldHeld() =>
        Player()?.GetComponent<InventoryComponent>()?.CountOf(GameIds.Currency.Gold) ?? 0;

    /// <summary>The already-held line differs per kind: a rested bed and a bought mount are not the same
    /// sentence, and a shared "nothing to do here" would read as the service being broken.</summary>
    private static string HeldKey(ServiceKind kind) => kind switch
    {
        ServiceKind.Trainer => "service.prompt_taught",
        ServiceKind.Bank => "service.prompt_open",
        ServiceKind.Stable => "service.prompt_owned",
        _ => "service.prompt_free",
    };

    private static string OfferKey(ServiceKind kind) => kind switch
    {
        ServiceKind.Trainer => "service.prompt_train",
        ServiceKind.Bank => "service.prompt_account",
        ServiceKind.Stable => "service.prompt_buy_mount",
        _ => "service.prompt_rest",
    };

    private static Player.PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out Player.PlayerCharacter player)
            ? player
            : null;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
