using Godot;

namespace Embervale.Economy;

/// <summary>
/// A paid service the player can walk up to (Phase 38D), authored under <c>data/services/</c> and
/// indexed by <see cref="ServiceDatabase"/>. A <see cref="ServiceComponent"/> names one by id, so a new
/// trainer or innkeeper is a <c>.tres</c> plus a component in a scene, with no code — the same shape
/// <see cref="ShopResource"/> and <c>PropertyResource</c> have.
///
/// Kind-specific fields sit on the one resource rather than in four subclasses, which is how
/// <c>WorldEventResource</c> carries a cache's item beside a raid's spawn counts. The validator is what
/// keeps an unused field from being authored by mistake.
/// </summary>
[GlobalClass]
public partial class ServiceResource : Resource
{
    /// <summary>Stable id, e.g. <c>service.ember_crown.inn</c>.</summary>
    [Export] public string Id { get; set; } = "service.unknown";

    /// <summary>Player-facing name. A <c>Loc</c> key — it reaches the interaction prompt, and CLAUDE.md
    /// §6 admits no literals there.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Which of the four verbs this is.</summary>
    [Export] public ServiceKind Kind { get; set; } = ServiceKind.Inn;

    [ExportGroup("Price")]

    /// <summary>
    /// Gold it costs, before standing. <c>0</c> is a genuinely free service. Discounted through
    /// <see cref="ShopPricing.ServicePrice"/>, which floors a priced service at 1 so a discount can
    /// never make one free.
    /// </summary>
    [Export] public int PriceGold { get; set; }

    /// <summary>
    /// Whose standing prices it, and who refuses to serve a hostile player (a <c>faction.*</c> id).
    /// Empty means standing has no effect. Authored here rather than read off the host entity's
    /// <c>FactionComponent</c> for the reasons <see cref="ShopResource.FactionId"/> gives.
    /// </summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    /// <summary>
    /// Story flag recording that this has been bought (a one-off purchase such as a bank account or a
    /// mount). Empty means <b>pay every time</b>, which is right for a night's rest or a lesson.
    ///
    /// ⚠️ A one-off service <em>must</em> author one, and <c>--validate</c> enforces it: with nothing
    /// recording that the purchase already happened, it charges again on every interaction. That is
    /// precisely the bug 36E fixed for boss rewards, and it is the same fix — a flag is the receipt.
    /// </summary>
    [Export] public string UnlockFlagId { get; set; } = string.Empty;

    [ExportGroup("Inn")]

    /// <summary>
    /// Hour (0–23) a night's rest ends at. Resting always moves the clock <em>forward</em> to it —
    /// see <see cref="ServiceRules.RestTarget"/>, which is where the +24 lives and why.
    /// </summary>
    [Export] public int RestHour { get; set; } = 8;

    [ExportGroup("Trainer")]

    /// <summary>
    /// Recipes this trainer teaches, through <c>CraftingComponent.Learn</c> — the method that has had
    /// <b>no caller since Phase 15</b> and whose absence is why <c>GameIds.Recipes.Starting</c> has been
    /// the whole of recipe reachability. A recipe here is now a second reachable path, and
    /// <c>ContentValidator</c> reads this list as part of that union.
    /// </summary>
    [Export] public Godot.Collections.Array<string> TaughtRecipeIds { get; set; } = new();

    /// <summary>
    /// Experience granted per lesson. This is how a trainer sells "points": it goes through
    /// <c>ProgressionComponent.AddXp</c>, so points arrive by <em>levelling</em>, never by purchase.
    /// <c>docs/DESIGN.md</c> §6 forbids buying a perk rank for coin and this is the reading that
    /// honours it — a trainer sells access and effort, not power.
    /// </summary>
    [Export] public int XpReward { get; set; }

    [ExportGroup("Passage")]

    /// <summary>
    /// Story flag a <see cref="ServiceKind.Passage"/> grants (Phase 38M). Separate from
    /// <see cref="UnlockFlagId"/> because that one doubles as the <em>receipt</em>: a bribe recorded
    /// there would refuse to sell a second time, and a bribe you can only pay once is a permit.
    /// So a permit authors <see cref="UnlockFlagId"/> and nothing here; a bribe authors this and
    /// leaves <see cref="UnlockFlagId"/> empty, and is sold again the moment the last one is spent.
    /// </summary>
    [Export] public string GrantedFlagId { get; set; } = string.Empty;

    /// <summary>
    /// Standing this costs (negative) or earns with <see cref="FactionId"/>. A bribe is a service
    /// whose price is partly paid in reputation — and because 38C prices every merchant in the realm
    /// off the same standing, the cheap crossing is charged for twice over at every counter in town.
    /// That is the whole of "a bribe costs standing"; no second currency was needed to say it.
    /// </summary>
    [Export] public int ReputationDelta { get; set; }

    [ExportGroup("Commission")]

    /// <summary>
    /// The station a <see cref="ServiceKind.Commission"/> master works at (Phase 38Q). The window he
    /// opens is the ordinary crafting window filtered to this station, so what he will make is exactly
    /// what the player could make standing at one — <c>CraftingPanel.StationShows</c> and
    /// <c>CraftingComponent.Knows</c> already answer it and no second list is authored.
    ///
    /// ⚠️ <c>Hand</c> is refused by <c>--validate</c>: a hand recipe crafts anywhere, so a counter
    /// offering only those charges for something the player can do standing still.
    /// </summary>
    [Export] public Crafting.CraftingStationType CommissionStation { get; set; } =
        Crafting.CraftingStationType.Forge;

    /// <summary>
    /// The shop whose prices the master's materials come out of (a <c>shop.*</c> id) — normally his own
    /// counter. Authored as a shop rather than as a second markup field so that his standing discount
    /// and his specialty reach a commission exactly as they reach his shelf, with no second ramp to
    /// drift out of step. <see cref="PriceGold"/> is the labour on top.
    ///
    /// ⚠️ <b>He is not required to stock what he supplies, and that is deliberate.</b> Commission
    /// materials come out of the back, not off the shelf; requiring stock would make a master's
    /// usefulness depend on 38B's restock clock, so the day he sold out he would also stop being able
    /// to make anything. The shop is consulted for its <em>prices</em> only.
    /// </summary>
    [Export] public string MaterialsShopId { get; set; } = string.Empty;

    [ExportGroup("Contracts")]

    /// <summary>
    /// How many supply contracts a <see cref="ServiceKind.Contracts"/> board shows at once (Phase
    /// 38Q2). ⚠️ <c>--validate</c> insists the authored pool holds <em>more</em> contracts than this,
    /// or a rotation would have to show one posting on two slots.
    /// </summary>
    [Export] public int BoardSlots { get; set; } = 3;

    /// <summary>
    /// How many days a set of postings stays up. It is also the deadline, and the only one: a posting
    /// is up until the board turns and then it is gone, which is why nothing about a contract is
    /// accepted, due or lapsed.
    ///
    /// ⚠️ The rotation is <b>derived</b> from this and the day (<see cref="ContractRules.Cycle"/>) and
    /// is never stored, so a quickload cannot reroll the board. Changing this number changes which
    /// posting sat on which slot for every past cycle too — harmless, because nothing saved refers to
    /// it, but worth knowing before blaming the board for looking wrong after an edit.
    /// </summary>
    [Export] public int RotationDays { get; set; } = 4;
}
