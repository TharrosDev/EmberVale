namespace Embervale.Economy;

/// <summary>
/// Whether a merchant is talked down today, and by how much (Phase 38S). Pure and Godot-free like
/// <see cref="WagerRules"/>, <see cref="ContractRules"/> and <see cref="ShopPricing"/> — the test
/// project throws an <c>AccessViolationException</c> constructing any Godot object, so this is the
/// half of the feature that can actually be tested.
///
/// ⚠️ <b>THE OUTCOME IS DERIVED FROM THE DAY AND THE SHOP, NEVER ROLLED AND NEVER SAVED</b>
/// (38Q2's board, 38R2's bones, and the same argument a third time). A quickload therefore
/// <em>replays</em> a refusal rather than rerolling it: the player may decline to open their mouth,
/// but not fish for a better mood. ⚠️ And that is only half the job — nothing in it stops a hundred
/// attempts in one afternoon. <see cref="HaggleLedger"/> does, by remembering that the player already
/// tried today. <b>Derive, then bound: two mechanisms, neither substituting for the other.</b>
///
/// <b>A haggle is a multiplier over a spread, which is why the 38A clamps still cover it.</b> This is
/// the invariant-3 question asked and answered: a commission was dangerous because it related the
/// prices of <em>two</em> different things, while a struck deal only moves the markup and the fraction
/// over one item's value — so <see cref="ShopPricing.BuyPrice"/>'s <c>&gt;= 1</c> clamp and
/// <see cref="ShopPricing.SellPrice"/>'s <c>0..1</c> clamp keep <c>sell &lt;= value &lt;= buy</c> true
/// however it is authored, and the sweep in <c>ShopPricingTests</c> proves it with the haggle folded in.
/// What a haggle <em>can</em> do is close the round-trip margin to nothing, which is a content bug the
/// arithmetic cannot see — so <c>--validate</c> holds every haggling shop to it, at <b>Allied</b>.
/// </summary>
public static class HaggleRules
{
    /// <summary>
    /// What a struck deal takes off the asking price. Deliberately smaller than a tier of standing
    /// (5%) is generous by, and smaller than the specialty premium: a day's negotiation should be
    /// worth having and never worth more than a reputation earned over a campaign.
    /// </summary>
    public const float BuyDiscount = 0.90f;

    /// <summary>
    /// What a struck deal adds to what the merchant pays. ⚠️ <b>This is the first thing in the game to
    /// move the SELL side of the spread</b> — standing deliberately does not (<c>ShopPricing.MarkupFor</c>
    /// says why: with both clamps in play a generous fraction converges on <c>sell == buy</c>). A haggle
    /// may, because it is bounded to one day and one merchant by the ledger, and because a negotiation
    /// the player can only ever use while buying would be half a feature to anyone selling loot.
    /// It is smaller than the buy-side discount for that convergence reason.
    /// </summary>
    public const float SellBonus = 1.10f;

    /// <summary>Keeps a shop's day distinct from a gambling house's throw, so the two derived outcomes
    /// in the economy cannot line up on an id that appears in both.</summary>
    private const int Salt = 0x48414747;   // 'HAGG'

    /// <summary>
    /// Whether <paramref name="shopId"/> is talked down on <paramref name="day"/>. A chance of
    /// <c>0</c> is a merchant who does not haggle at all, which is every shop authored before 38S.
    /// </summary>
    public static bool Succeeds(int day, string shopId, int chancePercent) =>
        chancePercent > 0 &&
        (chancePercent >= 100 || StableRoll.Percent(day, Salt, shopId) < (uint)chancePercent);

    /// <summary>The markup multiplier for a day's negotiation — <c>1</c> when no deal was struck, so
    /// the call sites have no branch of their own and cannot apply the discount twice.</summary>
    public static float BuyFactor(bool struck) => struck ? BuyDiscount : 1f;

    /// <summary>The sell-fraction multiplier for a day's negotiation. See <see cref="BuyFactor"/>.</summary>
    public static float SellFactor(bool struck) => struck ? SellBonus : 1f;
}
