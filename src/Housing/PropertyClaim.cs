namespace Embervale.Housing;

/// <summary>Why a deed can or cannot be claimed right now.</summary>
public enum ClaimOutcome
{
    /// <summary>The player already holds it.</summary>
    AlreadyOwned,

    /// <summary>A required quest has not been completed.</summary>
    QuestLocked,

    /// <summary>Affordable in principle, but the player is short of gold.</summary>
    TooExpensive,

    /// <summary>Claimable now.</summary>
    Granted,
}

/// <summary>
/// Whether a property can be claimed, and if not, which reason to say out loud (Phase 37A). Pure, so
/// the order of the checks — which is the whole of the behaviour — is pinned by tests rather than by
/// reading, the same way <see cref="Enemies.BossDefeat.Resolve"/> and
/// <see cref="Enemies.BossPhases.SelectPhase"/> are.
///
/// The <b>order matters and is deliberate</b>: a deed the player cannot afford should say so only
/// once they are actually allowed to buy it. Reporting the price first and the quest gate second
/// would tell a player to go and earn 2,500 gold for something a quest is holding shut anyway.
/// </summary>
public static class PropertyClaim
{
    /// <summary>
    /// Resolves a claim attempt. <paramref name="questDone"/> is <c>true</c> when the property
    /// authors no required quest, so an ungated deed simply falls through to the price.
    /// </summary>
    public static ClaimOutcome Resolve(bool owned, bool questDone, int goldHeld, int priceGold)
    {
        if (owned)
        {
            return ClaimOutcome.AlreadyOwned;
        }

        if (!questDone)
        {
            return ClaimOutcome.QuestLocked;
        }

        // A non-positive price is a property that is granted rather than sold — the quest above was
        // its cost. Guarded rather than assumed: a negative price must never pay the player.
        return priceGold > 0 && goldHeld < priceGold
            ? ClaimOutcome.TooExpensive
            : ClaimOutcome.Granted;
    }

    /// <summary>Gold actually taken for a granted claim — never negative, so a mis-authored price
    /// cannot hand the player money. The validator rejects one, this makes it harmless anyway.</summary>
    public static int PriceToCharge(int priceGold) => priceGold > 0 ? priceGold : 0;
}
