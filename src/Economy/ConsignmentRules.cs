using System;

namespace Embervale.Economy;

/// <summary>
/// What a broker pays and when (Phase 38P). Pure and Godot-free like <see cref="ShopPricing"/>,
/// <see cref="ShopStock"/> and <see cref="ServiceRules"/> — prices are player-facing numbers, so the
/// rounding is pinned by <c>ConsignmentRulesTests</c> rather than by reading.
///
/// <b>Consignment exists because every counter in the realm is capped twice</b>: by 38C's merchant
/// purse (she runs out of coin) and by 38H's saturation (her appetite falls as a stack is dumped on
/// her). A player back from the wilds with one expensive thing has nowhere to put it. A broker fronts
/// nothing, so neither cap applies — she takes the item now, charges a commission, and pays out when
/// it sells some days later.
///
/// ⚠️ <b>This cannot invert the realm's spread, and that is load-bearing.</b>
/// <see cref="Gross"/> routes through <see cref="ShopPricing.SellPrice"/>, whose fraction is clamped
/// to <c>0..1</c> — so a consignment payout is at most the item's value, whatever a <c>.tres</c>
/// authors, and <c>sell &lt;= value &lt;= buy</c> still holds realm-wide. 38G's regional demand is
/// still the only thing that can turn a carry positive. The clamp earns its keep a third time.
/// </summary>
public static class ConsignmentRules
{
    /// <summary>
    /// The shelf price before the house takes its cut — the same call the vendor window makes for an
    /// ordinary sale, at the broker's keener fraction.
    ///
    /// <b>Reused rather than reimplemented</b>: the <c>0..1</c> clamp inside it is the whole of the
    /// paragraph above, and a second rounding rule for the same question is how two numbers drift.
    /// </summary>
    public static int Gross(int baseValue, float fraction) => ShopPricing.SellPrice(baseValue, fraction);

    /// <summary>
    /// What reaches the player once the house has taken its commission. Rounds the <em>cut</em> up and
    /// floors the payout at <c>0</c>: rounding the cut down would let a 1-gold trinket at any
    /// commission pay its full shelf price, which is a broker working for free.
    ///
    /// Two steps rather than one folded fraction on purpose — the vendor window shows the player what
    /// the house takes, and it cannot show a number that was never computed.
    /// </summary>
    public static int Net(int gross, float commission)
    {
        int safe = Math.Max(0, gross);
        int cut = (int)Math.Ceiling(safe * Math.Clamp(commission, 0f, 1f));

        return Math.Max(0, safe - cut);
    }

    /// <summary>
    /// Whether a listing has sold yet.
    ///
    /// <b>It is <see cref="ShopStock.IsRestockDue"/> under another name and it calls it directly.</b>
    /// The question is identical — has <paramref name="days"/> elapsed since a stamped day — down to
    /// the two edge cases that function already learned the hard way: the <c>long</c> widening so a
    /// never-stamped <c>int.MinValue</c> does not overflow back to "not due", and a clock that has
    /// gone backwards counting as due rather than freezing for the rest of the run. Writing a second
    /// day-arithmetic function here would have had to rediscover both.
    /// </summary>
    public static bool HasSold(int dayListed, int currentDay, int days) =>
        ShopStock.IsRestockDue(dayListed, currentDay, days);
}
