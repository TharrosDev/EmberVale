using System;

namespace Embervale.Economy;

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
