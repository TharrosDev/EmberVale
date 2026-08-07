using System;
using System.Collections.Generic;
using Embervale.Factions;

namespace Embervale.Economy;

/// <summary>
/// Why a stock row is not for sale (Phase 38I). <see cref="Open"/> is the sellable case; the other
/// three each name a different thing to go and do, which is the whole reason they are not one boolean.
/// </summary>
public enum StockLock
{
    Open,
    Flag,
    Standing,
    Investment,
}

/// <summary>
/// When a shop restocks, and how good its leveled pool rolls (Phase 38B). Godot-free for the same
/// reason <see cref="ShopPricing"/> is: the test project may not construct Godot objects, and both of
/// these are off-by-one-shaped decisions that no amount of reading proves correct.
/// </summary>
public static class ShopStock
{
    /// <summary>
    /// Level at which the leveled pool reaches its best quality. Beyond it a merchant's stall stops
    /// improving — quality drives <see cref="Loot.LootRarity.Select"/>, so an unbounded ramp turns a
    /// general-goods stall into a Legendary vending machine for a high-level player.
    /// </summary>
    public const int QualityCapLevel = 20;

    /// <summary>
    /// Whether enough in-game days have passed. <c>restockDays &lt;= 0</c> is a shop that never
    /// restocks, which is only legal when every row is unlimited (the validator enforces that).
    ///
    /// The third case is the one worth having a function for: <b>a current day behind the stamp
    /// restocks too.</b> A quickload rewinds the clock while the service may still hold a stamp from
    /// the abandoned timeline, and comparing only forwards would leave that shop frozen for the rest
    /// of the run — a bug that would look like the restock feature simply not working.
    ///
    /// The subtraction widens to <c>long</c> deliberately. A never-stocked shop is stamped
    /// <c>int.MinValue</c>, and <c>0 - int.MinValue</c> overflows back to a negative in <c>int</c>, so
    /// the plain version answered "not due" for the one case that most obviously is. The test for it
    /// failed on the first run.
    /// </summary>
    public static bool IsRestockDue(int lastRestockDay, int currentDay, int restockDays)
    {
        if (restockDays <= 0)
        {
            return false;
        }

        return currentDay < lastRestockDay || (long)currentDay - lastRestockDay >= restockDays;
    }

    /// <summary>
    /// Extra quality handed to <c>LootGenerator.Generate</c> for a leveled pool: <c>0</c> at level 1,
    /// climbing to <c>1</c> at <see cref="QualityCapLevel"/> and held there.
    ///
    /// This is the <b>first player-level-driven scaling in the game</b> — nothing else reads
    /// <c>ProgressionComponent.Level</c> to scale anything, despite <c>LootRarity.Select</c>'s comment
    /// having claimed for several phases that quality came partly from "enemy level". It moves rarity
    /// and affix rolls, not which items are stocked; what a merchant deals in stays authored.
    /// </summary>
    public static float QualityForLevel(int level)
    {
        int steps = Math.Max(0, level - 1);
        return Math.Clamp(steps / (float)(QualityCapLevel - 1), 0f, 1f);
    }

    /// <summary>
    /// How little a merchant will pay for the thing they are sickest of (Phase 38H). The price falls
    /// this far and no further — saturation is a slope, never a refusal, so the player is always deciding
    /// rather than being told no.
    /// </summary>
    public const float SaturationFloor = 0.4f;

    /// <summary>
    /// Units of one item a merchant takes at full price before their appetite starts to fall.
    ///
    /// This band exists so the mechanic bites on <b>repeat volume</b> rather than on one honest haul. A
    /// player who hunts, fills a stack and sells it should not be docked for selling tidily — without a
    /// grace band an ordinary stack of ten took a 30% haircut on the first sale of the day, which reads
    /// as a punishment for playing normally rather than as a market filling up.
    /// </summary>
    public const int SaturationGrace = 8;

    /// <summary>Units of decay after <see cref="SaturationGrace"/> before hitting
    /// <see cref="SaturationFloor"/>.</summary>
    public const int SaturationSpan = 30;

    /// <summary>
    /// What a merchant will still pay for one more of something they have been buying all day (38H):
    /// full price for the first <see cref="SaturationGrace"/> units, then falling linearly to
    /// <see cref="SaturationFloor"/> across <see cref="SaturationSpan"/> more, and holding there.
    ///
    /// The grace band is what keeps this a pressure rather than a tax. One hunt's worth of pelts sells at
    /// very nearly full price; it is the third trip back with the same goods that stops being worth the
    /// walk. There is no cliff at the end of the band — the slope starts at zero gradient, so the player
    /// never sees a sudden drop they have to count units to predict.
    ///
    /// ⚠️ <b>A shop that never restocks does not saturate at all.</b> Absorption is cleared by the restock
    /// clock, so without one it would decay to the floor and stay there for the rest of the run — a
    /// permanent markdown that reads as the merchant being broken rather than as a mechanic. The guard
    /// lives here rather than at a call site so a future caller cannot forget it, which is also why 38H
    /// needs no validator rule: the impossible case is answered in the arithmetic instead of rejected in
    /// the data.
    /// </summary>
    public static float SaturationMultiplier(int absorbed, int restockDays)
    {
        if (restockDays <= 0 || absorbed < SaturationGrace)
        {
            return 1f;
        }

        float fallen = (absorbed - SaturationGrace) * (1f - SaturationFloor) / SaturationSpan;
        return Math.Max(SaturationFloor, 1f - fallen);
    }

    /// <summary>
    /// What a merchant pays for a whole stack, <b>decaying across the stack's own units</b> (38H).
    ///
    /// ⚠️ This granularity is the entire point. A stack used to be one price multiplied by a quantity, and
    /// applying saturation at that level would price all twenty hides at the pre-sale multiplier — making
    /// "dump the whole stack in one click" strictly optimal and punishing only the player who sells
    /// tidily. Summing per unit as the count climbs means selling twenty at once and twenty one at a time
    /// pay exactly the same, which is what <c>StackSaleMatchesSellingOneAtATime</c> pins.
    ///
    /// <paramref name="unitPrice"/> is what <see cref="ShopPricing.SellPrice"/> already returned for one
    /// unit, so rarity, affixes, the spread and 38F's specialty premium are all folded in before this is
    /// reached. At <c>absorbed = 0</c> on a fresh shop this returns exactly
    /// <c>unitPrice * quantity</c> — 38A's arithmetic, unregressed.
    /// </summary>
    public static int SaturatedPayout(int unitPrice, int absorbed, int quantity, int restockDays)
    {
        if (unitPrice <= 0 || quantity <= 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < quantity; i++)
        {
            // ⚠️ Each unit floors at 1, not at 0. A goblin hide sells for a single coin, so *any*
            // multiplier below 1 would round it to nothing — and the panel refuses a zero payout as
            // worthless (38A, deliberately: handing an item over for free is item loss wearing a
            // transaction's clothes). Without this floor, saturation quietly becomes a refusal for
            // exactly the cheap, high-volume goods it is meant to be about, and the failure looks like
            // the merchant breaking rather than like a market.
            int unit = (int)Math.Floor(unitPrice * SaturationMultiplier(absorbed + i, restockDays));
            total += Math.Max(1, unit);
        }

        return total;
    }

    /// <summary>
    /// Which gate, if any, is holding a stock row shut (Phase 38I).
    ///
    /// ⚠️ <b>The order is the feature.</b> A row behind all three gates reports the story flag first,
    /// then standing, then gold — the same rule <c>PropertyClaim.Resolve</c> follows, so a player is
    /// never sent to earn coin for something a story beat is holding shut. Collapsing the three into
    /// one "locked" would tell a player to go and invest in a merchant who is waiting on a quest.
    ///
    /// Every parameter is a plain value so the test project can reach it, exactly as
    /// <see cref="ShopPricing"/>'s are — the shop resource and the player's components are read at the
    /// call site.
    /// </summary>
    public static StockLock LockOf(
        ReputationTier requiredTier,
        string requiredFlagId,
        int requiredInvestment,
        ReputationTier standing,
        bool hasFlag,
        int invested)
    {
        if (!string.IsNullOrEmpty(requiredFlagId) && !hasFlag)
        {
            return StockLock.Flag;
        }

        if (standing < requiredTier)
        {
            return StockLock.Standing;
        }

        if (invested < requiredInvestment)
        {
            return StockLock.Investment;
        }

        return StockLock.Open;
    }

    /// <summary>
    /// The purse bonus earned by the rungs of a stake the player actually holds (Phase 38I). Takes the
    /// bonuses as plain numbers rather than the authored sub-resources so this file stays Godot-free.
    ///
    /// Clamps rather than throws on a count past the end of the ladder: a save carrying more rungs than
    /// the shop still authors is a content edit, not a corruption, and the player keeps what they paid
    /// for up to what exists.
    /// </summary>
    public static int PurseBonusThrough(IReadOnlyList<int> bonuses, int invested)
    {
        int total = 0;
        int held = Math.Min(invested, bonuses.Count);
        for (int i = 0; i < held; i++)
        {
            total += Math.Max(0, bonuses[i]);
        }

        return total;
    }

    /// <summary>
    /// The purse a restock refills to once a stake is taken into account (Phase 38I).
    ///
    /// ⚠️ <b>An unlimited purse stays unlimited.</b> A merchant who authors no purse buys anything at
    /// all, so adding a bonus to it would make her <em>finite</em> — a downgrade the player paid gold
    /// for, and the exact opposite of what a stake promises. The guard lives here rather than at the
    /// call site so a future caller cannot forget it, the same reason
    /// <see cref="SaturationMultiplier"/>'s never-restocks case does. <c>--validate</c> still rejects
    /// the authoring, because the arithmetic being safe does not make the data meaningful.
    /// </summary>
    public static int PurseAfterInvestment(int authoredPurse, int bonus) =>
        authoredPurse <= 0 ? UnlimitedPurse : authoredPurse + Math.Max(0, bonus);

    /// <summary>Sentinel for a merchant who authors no purse and can buy anything.</summary>
    public const int UnlimitedPurse = -1;

    /// <summary>
    /// Whether a merchant holding <paramref name="purse"/> can cover a payout (Phase 38C).
    /// <see cref="UnlimitedPurse"/> covers everything, and a non-positive payout is nothing to cover.
    ///
    /// Extracted here rather than left inside <see cref="ShopStockService"/> for the reason the whole
    /// file exists: the service is a Godot <c>Node</c>, so the test project cannot construct one, and
    /// "can the merchant afford this" is a boundary comparison that no amount of reading proves.
    /// </summary>
    public static bool CanCover(int purse, int amount) =>
        purse < 0 || amount <= 0 || purse >= amount;

    /// <summary>What the purse holds after spending on a sale that went through.</summary>
    public static int AfterSpend(int purse, int amount) =>
        purse < 0 || amount <= 0 ? purse : Math.Max(0, purse - amount);

    /// <summary>
    /// What the purse holds after handing gold back for a sale that debited and then could not complete.
    /// Clamped to what the shop authored, so a failed sale can never mint the merchant money — the
    /// mirror of the buy path's refund never minting the player any.
    /// </summary>
    public static int AfterRefund(int purse, int amount, int authoredPurse) =>
        purse < 0 || amount <= 0 ? purse : Math.Min(Math.Max(0, authoredPurse), purse + amount);
}
