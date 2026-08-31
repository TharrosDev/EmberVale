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

    /// <summary>
    /// A holding the player must own before this counter will serve them (Phase 37E).
    /// <b>Empty is ungated</b>, which is every service authored before 37E — so the field arrives
    /// without changing a single existing one, the same shape <c>ShopResource.CellId</c> took.
    ///
    /// It exists for the bed in the player's own house. ⚠️ <b>Without it a free service standing in a
    /// private room is free to everyone who walks in</b>, and this component was the only one of the
    /// three property-aware interactables with no ownership check —
    /// <see cref="Housing.PropertyStorageComponent"/> and <see cref="Housing.TrophyStandComponent"/>
    /// have both asked <see cref="HousingService.Owns"/> since 37B.
    ///
    /// ⚠️ <b>The gate lives on the component, not in <see cref="TryUse"/>, and that is deliberate.</b>
    /// <c>TryUse</c> is static because <c>DialogueEffect.OpenService</c> reaches it with no component
    /// at all — a service fired from a conversation has no holding to belong to, so it is ungated by
    /// nature rather than by omission.
    ///
    /// ⚠️ <c>--validate</c> cannot see this: it lives in a <c>.tscn</c>, which the validator does not
    /// scan — the same limit <c>VendorComponent.ShopId</c> has carried since 38A.
    /// </summary>
    [Export] public string PropertyId { get; set; } = string.Empty;

    /// <summary>Whether this counter is standing in someone else's house. Answers <c>false</c> when
    /// no holding is named, so an ungated service never consults the housing service at all.</summary>
    private bool NotMine =>
        PropertyId.Length > 0 && !(Resolve<HousingService>()?.Owns(PropertyId) ?? false);

    public override string Prompt
    {
        get
        {
            if (ServiceDatabase.Get(ServiceId) is not { } service)
            {
                return string.Empty; // authoring faults belong in the log, not in the player's face
            }

            string name = Loc.T(service.NameKey);

            // 37E: answered before anything else, and it NAMES itself rather than going quiet. A bed
            // that says nothing when you look at it reads as broken furniture; one that says whose it
            // is teaches the player that the house is for sale.
            if (NotMine)
            {
                return Loc.TF("service.not_yours", name);
            }

            int price = PriceOf(service);

            // 38U, and the one place the gate's word "hover" cannot be honoured: this is a world
            // interaction prompt, not a Control, so there is nothing to put a tooltip on. The rule
            // underneath it still applies — a price that moved says why it moved — so the reason rides
            // inline on the lines that quote a number, and on no others.
            string note = StandingNote(service);

            return Evaluate(service) switch
            {
                ServiceOutcome.Unknown => string.Empty,
                ServiceOutcome.Hostile => Loc.TF("service.prompt_hostile", name),
                // ⚠️ 38R's one prompt state that is not an outcome. A full party is neither
                // "already hired" nor "cannot afford" — the press would simply do nothing, which is
                // 38J's dead-choice failure. Kept out of AlreadyHeld because "you already travel with
                // her" would send a player looking for someone they have never met.
                ServiceOutcome.Granted when service.Kind == ServiceKind.Mercenary && PartyIsFull() =>
                    Loc.TF("service.prompt_mercenary_full", name),
                // 38R2: the only offer line with a third argument, so it gets its own arm rather than
                // a fourth key-to-argument table. The throws left are the interesting number at a
                // gambling table — the stake alone would make three presses look like one.
                ServiceOutcome.Granted when service.Kind == ServiceKind.Wager =>
                    Loc.TF("service.prompt_wager", name, price, ThrowsLeft(service)) + note,
                ServiceOutcome.AlreadyHeld => Loc.TF(HeldKey(service.Kind), name),
                ServiceOutcome.CannotAfford =>
                    Loc.TF("service.prompt_price", name, price, GoldHeld()) + note,
                // ⚠️ A free service falls back to the bare "Use {0}" line, which is right for a free
                // bed and wrong for a warden's search — the one service the player is not buying, and
                // the only one where not knowing what the press does costs them their goods (38O).
                // The gold owed rides along as {1} for the one free service that has a number to
                // report (38P's clerk). Every other free line ignores it, which is what string.Format
                // does with a spare argument — cheaper than a third key-to-argument table.
                _ => price > 0
                    ? Loc.TF(OfferKey(service.Kind), name, price) + note
                    : Loc.TF(FreeKey(service.Kind), name, DueGold()),
            };
        }
    }

    public override bool Interact(IEntity instigator)
    {
        // ⚠️ Re-checked on the press, not trusted from the prompt — the same rule every Phase 37
        // refusal follows. A prompt is a frame old and ownership can change between the two.
        if (NotMine)
        {
            return false;
        }

        if (ServiceDatabase.Get(ServiceId) is not { } service)
        {
            return false;
        }

        // The vault is the host entity's own inventory (38D), which is the one thing a service
        // needs that only the component standing in the world can supply — see TryUse.
        // TryUse itself refuses on price, cooldown and stock, so its answer is the interaction's.
        return TryUse(service, instigator, Entity?.GetComponent<InventoryComponent>());
    }

    /// <summary>
    /// Runs a service: the ordered refusal, the charge and the verb (Phase 38D, extracted in 38R).
    ///
    /// ⚠️ <b><paramref name="vault"/> is why this is not a pure extraction.</b> Every other verb works
    /// off the player and the resource, but <see cref="ServiceKind.Bank"/> opens the <em>host entity's</em>
    /// inventory — and a service fired from a conversation
    /// (<see cref="Dialogue.DialogueEffect.OpenService"/>) has no host entity to open. Rather than
    /// invent a vault at runtime, the parameter is nullable, a bank without one logs and does nothing,
    /// and <c>--validate</c> refuses the authoring that would reach it.
    /// </summary>
    /// <returns>True when the service was actually rendered; false on every refusal (not
    /// granted, no pack, the fee could not be taken). The interaction publishes on that answer.</returns>
    public static bool TryUse(ServiceResource service, IEntity instigator, InventoryComponent? vault)
    {
        if (Evaluate(service) != ServiceOutcome.Granted ||
            instigator.GetComponent<InventoryComponent>() is not { } pack)
        {
            return false; // the prompt has already said why
        }

        // Charged before the verb, and both halves are separate conditions for the reason
        // PropertyDeedComponent spells out: chained into one test, an unresolvable pack falls *through*
        // to a free service. Unlike a purchase there is nothing to roll back — a refill, a flag and a
        // taught recipe cannot fail after the gold is taken.
        // ⚠️ 38Q is the one kind that is NOT charged here, and it looks like an oversight beside the
        // seven above. A commission's verb can fail — a full pack refuses the piece and rolls the
        // whole craft back — and the fee is per piece, not per visit. Charged at the counter it would
        // take gold from a player who then commissions nothing, and take it once for a window they
        // might order five things from. CraftingComponent.Commission owns the money for that reason.
        // ⚠️ 38R adds the second exemption, and it is 38Q's reason rather than a new one: a hire fails
        // on a full party, so the money must follow the companion actually arriving. Two kinds is
        // still a list and not a mechanism — if a third appears, that is when it earns a field.
        int price = PriceOf(service);
        bool chargedAtTheCounter =
            service.Kind is not (ServiceKind.Commission or ServiceKind.Mercenary);
        if (chargedAtTheCounter && price > 0 && !pack.RemoveItem(GameIds.Currency.Gold, price))
        {
            return false; // the gold went somewhere between the prompt and the press; deliver nothing
        }

        switch (service.Kind)
        {
            case ServiceKind.Trainer:
                Train(service, instigator);
                break;
            case ServiceKind.Bank:
                OpenVault(service, instigator, vault);
                break;
            case ServiceKind.Inn:
                Rest(service, instigator);
                break;
            case ServiceKind.Passage:
                Passage(service, instigator);
                break;
            case ServiceKind.Search:
                Search(instigator);
                break;
            case ServiceKind.Redeem:
                Redeem(service, instigator, price);
                break;
            case ServiceKind.Collect:
                Collect(instigator);
                break;
            case ServiceKind.Appraise:
                EventBus.Instance?.Publish(new AppraisalOpenedEvent(instigator, Loc.T(service.NameKey)));
                break;
            case ServiceKind.Contracts:
                EventBus.Instance?.Publish(new ContractBoardOpenedEvent(
                    instigator, Loc.T(service.NameKey), service.BoardSlots, service.RotationDays));
                break;
            case ServiceKind.Mercenary:
                Hire(service, pack, price);
                break;
            case ServiceKind.Wager:
                Throw(service, instigator, pack, price);
                break;
            case ServiceKind.Commission:
                // The master's order desk is the ordinary crafting window with a fee on it, so this
                // adds no panel: CraftingPanel already lists what the player knows at a station, and
                // 38Q's two extra fields turn missing ingredients from a refusal into a line on a bill.
                EventBus.Instance?.Publish(new CraftingStationOpenedEvent(
                    instigator, service.CommissionStation, Loc.T(service.NameKey), price, service.MaterialsShopId));
                break;
            default:
                StableMount(service, instigator);
                break;
        }

        Log.Info(chargedAtTheCounter
            ? $"Service '{service.Id}' used for {price} gold."
            : $"Service '{service.Id}' opened at {price} gold a piece.");

        return true;
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
    private static void OpenVault(ServiceResource service, IEntity instigator, InventoryComponent? vault)
    {
        if (vault == null)
        {
            // Also the fail-safe for a bank reached through a conversation, which has no host entity
            // to carry a vault. --validate refuses that authoring, so this line means a .tscn fault.
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

    /// <summary>
    /// Sells passage through a tolled crossing (Phase 38M): the flag <c>TollFee</c> reads, plus the
    /// standing a bribe costs. The order matters in one small way — the reputation is moved
    /// <em>after</em> the flag is set, so a standing swing that turns the faction hostile cannot
    /// retroactively swallow the thing that was just paid for.
    /// </summary>
    private static void Passage(ServiceResource service, IEntity instigator)
    {
        Unlock(service, instigator);

        if (!string.IsNullOrEmpty(service.GrantedFlagId))
        {
            instigator.GetComponent<StoryFlagsComponent>()?.Set(service.GrantedFlagId);
        }

        if (service.ReputationDelta != 0 && !string.IsNullOrEmpty(service.FactionId))
        {
            instigator.GetComponent<ReputationComponent>()?.Add(service.FactionId, service.ReputationDelta);
        }
    }

    /// <summary>
    /// Searches the player and impounds whatever the law does not let them carry (Phase 38O). Free, so
    /// there is no charge to roll back, and the outcome is reported by the prompt flipping to "nothing
    /// to declare" the instant the pack is clean.
    /// </summary>
    private static void Search(IEntity instigator)
    {
        if (Resolve<ContrabandImpound>() is not { } impound ||
            instigator.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        impound.SeizeFrom(pack);
    }

    /// <summary>
    /// Gives the goods back for the fine already charged (Phase 38O).
    ///
    /// ⚠️ <b>The refund is the load-bearing part.</b> The fine was charged on every unit held, but a
    /// full pack cannot take every unit back — and a charge for goods the wardens still hold is item
    /// loss with a receipt, the exact failure <c>VendorPanel.Sell</c> refunds the vendor purse to
    /// avoid. The units that did not fit stay impounded and their share of the fine comes back, so a
    /// second visit with room finishes the job at no extra cost.
    ///
    /// The refund fits by construction in every case that matters: the gold was taken out of a stack
    /// that is either still there or whose slot has just been freed by emptying.
    /// </summary>
    private static void Redeem(ServiceResource service, IEntity instigator, int price)
    {
        if (Resolve<ContrabandImpound>() is not { } impound ||
            instigator.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        int units = impound.Units;
        int left = impound.ReturnTo(pack);
        if (left <= 0 || units <= 0 || price <= 0)
        {
            return;
        }

        int refund = (int)((long)price * left / units);
        if (refund > 0 && ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
        {
            pack.AddItem(gold, refund);
            Log.Info($"Service '{service.Id}': refunded {refund}g for {left} units that would not fit.");
        }
    }

    /// <summary>
    /// Hands over what the broker's shelf has earned (Phase 38P). Free, so there is nothing to roll
    /// back, and the ledger keeps whatever would not fit in the pack — the prompt then still reads as
    /// money waiting, which is the honest state.
    /// </summary>
    private static void Collect(IEntity instigator)
    {
        if (Resolve<ConsignmentLedger>() is not { } ledger ||
            instigator.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        ledger.Collect(CurrentDay(), pack);
    }

    /// <summary>
    /// Hires a sword (Phase 38R): the companion joins, and only then is the gold taken.
    ///
    /// ⚠️ <b>The order is the 38Q inversion and it needs no rollback here, which is the difference.</b>
    /// A commission's fee is charged from a window the player may leave open for minutes, so its gold
    /// has a real window to disappear in; this runs inside the same synchronous call as the
    /// affordability check, so once <c>Recruit</c> says yes the purse cannot have moved. The refusal
    /// path is the one that matters: a full party recruits nobody and is charged nothing.
    /// </summary>
    private static void Hire(ServiceResource service, InventoryComponent pack, int price)
    {
        if (Resolve<Companions.CompanionRoster>() is not { } roster ||
            !roster.Recruit(service.CompanionId))
        {
            // A full party, an unknown id, or no roster at all — nobody arrived, so nothing is owed.
            // The prompt already says so for the party-full case, which is the only one a player meets.
            return;
        }

        if (price > 0 && !pack.RemoveItem(GameIds.Currency.Gold, price))
        {
            Log.Warn($"Service '{service.Id}': hired '{service.CompanionId}' but could not take {price}g.");
        }
    }

    /// <summary>
    /// Takes a throw at a gambling house (Phase 38R2). The stake has already been taken by the caller,
    /// which is the ordinary ordering and is right here for the reason the two exceptions are not: a
    /// throw cannot fail, and a payout is gold, which stacks into any pack that had room for the stake.
    ///
    /// ⚠️ <b>The throw is recorded before it is resolved, and the ledger hands back the index it is
    /// resolved with.</b> Counting the throw here and asking <see cref="WagerRules.Won"/> with a
    /// separately-derived number would be two places that must agree about how many throws have
    /// happened — and the day the two disagreed, the same throw would pay twice.
    /// </summary>
    private static void Throw(ServiceResource service, IEntity instigator, InventoryComponent pack, int stake)
    {
        int day = CurrentDay();
        int index = Resolve<WagerLedger>() is { } ledger ? ledger.TakePlay(service.Id, day) : 0;
        bool won = WagerRules.Won(day, index, service.Id, service.WinPercent);

        if (won && ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
        {
            pack.AddItem(gold, service.PayoutGold);
        }

        EventBus.Instance?.Publish(new WagerSettledEvent(
            instigator, Loc.T(service.NameKey), won, won ? service.PayoutGold : stake));
    }

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

    private static ServiceOutcome Evaluate(ServiceResource? service)
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
        // 38O's two, answered before the flags because neither authors one: there is nothing to search
        // an empty-handed traveller for, and nothing to buy back from an empty impound. Both reach the
        // player as the "already held" prompt, which is the one state in the battery that means
        // "correct, and nothing to do" rather than a refusal.
        if (service.Kind == ServiceKind.Search)
        {
            return Player()?.GetComponent<InventoryComponent>() is not { } pack ||
                ContrabandImpound.ContrabandIn(pack) == 0;
        }

        if (service.Kind == ServiceKind.Redeem)
        {
            return ImpoundedUnits() == 0;
        }

        // 38P, the same shape: nothing has sold yet, so there is nothing to hand over. It reaches the
        // player as the "correct, and nothing to do" prompt rather than a refusal — a shelf full of
        // unsold goods is the system working, not a merchant turning them away.
        if (service.Kind == ServiceKind.Collect)
        {
            return DueGold() == 0;
        }

        // 38P2, answered from the pack the way Search's is: an appraiser with nothing to look at
        // would open an empty window. "Correct, and nothing to do" rather than a refusal.
        if (service.Kind == ServiceKind.Appraise)
        {
            return Player()?.GetComponent<InventoryComponent>() is not { } pack || !HasAnythingToValue(pack);
        }

        // 38Q, the Appraise shape again from the other end: an empty window is unreachable rather
        // than merely unlikely. A master will make anything the player knows how to make at his
        // station, so knowing none of it is "nothing to do here" — and it is answered from what the
        // player knows, never from what he stocks, because his materials come out of the back.
        if (service.Kind == ServiceKind.Commission)
        {
            return !KnowsAnythingFor(service.CommissionStation);
        }

        // 38R2: the day's allowance is spent. "Correct, and nothing to do" rather than a refusal —
        // the house has not turned the player away, tomorrow is a different day.
        if (service.Kind == ServiceKind.Wager)
        {
            return ThrowsLeft(service) <= 0;
        }

        // 38R: the roster is the record, so there is no flag to ask. A dismissal must put the hire
        // back on the market — a flag would survive it and retire her permanently.
        if (service.Kind == ServiceKind.Mercenary)
        {
            return Resolve<Companions.CompanionRoster>()?.IsRecruited(service.CompanionId) ?? false;
        }

        if (!string.IsNullOrEmpty(service.UnlockFlagId))
        {
            return Player()?.GetComponent<StoryFlagsComponent>()?.Has(service.UnlockFlagId) ?? false;
        }

        // 38M: a pass is repeatable but not stackable. Without this the gate-hand happily takes a
        // second bribe for a crossing already paid for, which is a refusal the player would only
        // discover by losing the gold.
        if (!string.IsNullOrEmpty(service.GrantedFlagId))
        {
            return Player()?.GetComponent<StoryFlagsComponent>()?.Has(service.GrantedFlagId) ?? false;
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

    /// <summary>
    /// The gold this costs at the player's standing. Every kind but one reads its authored price.
    ///
    /// ⚠️ <b><see cref="ServiceKind.Redeem"/> is priced from the impound, not from the resource</b>
    /// (38O): <c>PriceGold</c> is the per-unit fine, and the bill depends on how much was taken. It
    /// still goes through <see cref="ShopPricing.ServicePrice"/>, so the wardens' standing discount
    /// applies to a fine exactly as it does to a bed — there is no second discount ramp to drift.
    /// </summary>
    /// <summary>
    /// Why this service costs what it costs (38U) — empty at <see cref="ReputationTier.Neutral"/>,
    /// which is the tier that changes nothing and the tier every service is authored against.
    ///
    /// The percentage is derived from <see cref="ShopPricing.PriceMultiplierFor"/> rather than written
    /// out again, exactly as <c>VendorPanel.BuildStanding</c> derives its own — one ramp, two readers,
    /// nothing to keep in step by hand. A free service gets no note at all: a discount on nothing is
    /// noise, and <see cref="ShopPricing.ServicePrice"/> deliberately leaves a <c>0</c> at <c>0</c>.
    /// </summary>
    private static string StandingNote(ServiceResource service)
    {
        ReputationTier tier = StandingWith(service.FactionId);
        if (tier == ReputationTier.Neutral)
        {
            return string.Empty;
        }

        int percent = (int)System.Math.Round(
            (ShopPricing.PriceMultiplierFor(tier) - 1f) * 100f, System.MidpointRounding.AwayFromZero);

        return Loc.TF("service.price_standing", percent.ToString("+0;-0;0"));
    }

    private static int PriceOf(ServiceResource service)
    {
        int gold = service.Kind == ServiceKind.Redeem
            ? ContrabandLaw.Fine(service.PriceGold, ImpoundedUnits())
            : service.PriceGold;

        return ShopPricing.ServicePrice(gold, StandingWith(service.FactionId));
    }

    /// <summary>Whether the pack holds anything an appraiser could put a price on (38P2). Uses
    /// <see cref="ShopPricing.Sellable"/> — the same refusal the vendor window applies — so a pack of
    /// quest objects and coin reads as nothing to value rather than as an empty valuation.</summary>
    private static bool HasAnythingToValue(InventoryComponent pack)
    {
        foreach (ItemStack stack in pack.Stacks)
        {
            if (ShopPricing.Sellable(stack.Instance.Type, stack.Instance.TemplateId == GameIds.Currency.Gold))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the player knows a single recipe a master at <paramref name="station"/> could
    /// make (38Q). Reads the same two questions <c>CraftingPanel</c> filters its list by, so the
    /// prompt cannot promise a window the panel would then draw empty.</summary>
    private static bool KnowsAnythingFor(CraftingStationType station)
    {
        if (Player()?.GetComponent<CraftingComponent>() is not { } crafting)
        {
            return false;
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (crafting.Knows(recipe.Id) && CraftingComponent.StationAccepts(recipe.Station, station))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether there is no room in the band for one more (38R). An unresolvable roster reads
    /// as not full, which fails the same way <see cref="IsHostileTo"/> does — the refusal is
    /// <c>Recruit</c>'s to make, and a prompt that hides an offer in a half-built world is worse than
    /// one that makes it and is turned down.</summary>
    private static bool PartyIsFull() =>
        Resolve<Companions.CompanionRoster>() is { } roster && roster.Count >= roster.MaxPartySize;

    /// <summary>Throws left at this house today (38R2). Read by the already-held test and by the
    /// prompt, so what the walk-up line says and what the press does cannot drift.</summary>
    private static int ThrowsLeft(ServiceResource service) => WagerRules.PlaysLeft(
        Resolve<WagerLedger>()?.PlaysToday(service.Id, CurrentDay()) ?? 0, service.PlaysPerDay);

    private static int ImpoundedUnits() => Resolve<ContrabandImpound>()?.Units ?? 0;

    /// <summary>Gold the consignment shelf has earned and not yet handed over (38P). Read by both the
    /// prompt and the already-held test, so "nothing has sold yet" and an empty payout cannot
    /// disagree — the rule <c>PropertyDeedComponent</c> set for a deed.</summary>
    private static int DueGold() => Resolve<ConsignmentLedger>()?.DueGold(CurrentDay()) ?? 0;

    private static int CurrentDay() => Resolve<WorldClock>()?.Day ?? 0;

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
        ServiceKind.Passage => "service.prompt_passage_held",
        ServiceKind.Search => "service.prompt_search_clean",
        ServiceKind.Redeem => "service.prompt_redeem_empty",
        ServiceKind.Collect => "service.prompt_collect_empty",
        ServiceKind.Appraise => "service.prompt_appraise_empty",
        ServiceKind.Commission => "service.prompt_commission_none",
        ServiceKind.Mercenary => "service.prompt_mercenary_hired",
        ServiceKind.Wager => "service.prompt_wager_spent",
        // 38Q2 authors no key here on purpose: a board is never "already held". If this line is ever
        // reached, AlreadyHeld has grown a branch it should not have.
        _ => "service.prompt_free",
    };

    private static string OfferKey(ServiceKind kind) => kind switch
    {
        ServiceKind.Trainer => "service.prompt_train",
        ServiceKind.Bank => "service.prompt_account",
        ServiceKind.Stable => "service.prompt_buy_mount",
        ServiceKind.Passage => "service.prompt_passage",
        ServiceKind.Redeem => "service.prompt_redeem",
        // The price named here is the labour only — the materials line depends on what is in the pack
        // and on which recipe is chosen, so it belongs in the window rather than on a walk-up prompt.
        ServiceKind.Commission => "service.prompt_commission",
        ServiceKind.Mercenary => "service.prompt_mercenary",
        _ => "service.prompt_rest",
    };

    /// <summary>The offer line when there is nothing to pay. Only the search needs its own — every
    /// other free service is something the player chose to walk up to.</summary>
    private static string FreeKey(ServiceKind kind) => kind switch
    {
        ServiceKind.Search => "service.prompt_search",
        ServiceKind.Collect => "service.prompt_collect",
        ServiceKind.Appraise => "service.prompt_appraise",
        ServiceKind.Contracts => "service.prompt_contracts",
        _ => "service.prompt_free",
    };

    private static Player.PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out Player.PlayerCharacter player)
            ? player
            : null;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
