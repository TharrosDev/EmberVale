using System;

namespace Embervale.Economy;

/// <summary>Why a service can or cannot be used right now.</summary>
public enum ServiceOutcome
{
    /// <summary>The id resolved to nothing — an authoring fault, not something to say out loud.</summary>
    Unknown,

    /// <summary>Standing with the service's faction is hostile; they will not deal.</summary>
    Hostile,

    /// <summary>Already paid for, or there is nothing left to give.</summary>
    AlreadyHeld,

    /// <summary>Wanted, allowed, and unaffordable.</summary>
    CannotAfford,

    /// <summary>Go ahead.</summary>
    Granted,
}

/// <summary>
/// Whether a service can be used, and the clock arithmetic resting needs (Phase 38D). Pure and
/// Godot-free like <see cref="ShopPricing"/>, <see cref="ShopStock"/> and <see cref="TravelFee"/> — 38C
/// learned this the hard way when the vendor purse logic started inside a Godot <c>Node</c> and could
/// be neither unit-tested nor driven headlessly.
/// </summary>
public static class ServiceRules
{
    /// <summary>
    /// Resolves a use attempt. The <b>order is the behaviour</b>, which is why this is a function and
    /// not four scattered <c>if</c>s — the same reason <see cref="Housing.PropertyClaim.Resolve"/> is.
    ///
    /// Already-held comes <em>before</em> the price: telling a player who owns the mount to go and earn
    /// 400 gold for it sends them after the wrong thing, which is exactly the mistake 37A's deed
    /// ordering was written to avoid. Hostility comes before both, because a merchant who will not deal
    /// has no price to quote.
    /// </summary>
    public static ServiceOutcome Resolve(bool known, bool hostile, bool alreadyHeld, int price, int goldHeld)
    {
        if (!known)
        {
            return ServiceOutcome.Unknown;
        }

        if (hostile)
        {
            return ServiceOutcome.Hostile;
        }

        if (alreadyHeld)
        {
            return ServiceOutcome.AlreadyHeld;
        }

        return ShopPricing.CanAfford(price, goldHeld) ? ServiceOutcome.Granted : ServiceOutcome.CannotAfford;
    }

    /// <summary>
    /// The value to hand <c>WorldClock.SetTimeOfDay</c> to rest until <paramref name="restHour"/>.
    ///
    /// ⚠️ <b>This is the one genuinely subtle thing in the phase.</b> <c>SetTimeOfDay</c> is one-way by
    /// design — it advances <c>Day</c> only for an hour of 24 or more, and "jumping backwards sets the
    /// hour and leaves the date alone". So resting from 20:00 to 08:00 must be asked for as <c>32</c>,
    /// not <c>8</c>: passing <c>8</c> would rewind the hour, never advance the day, and therefore
    /// silently freeze 38B's shop restock clock and every future daily service. Nothing about that
    /// failure would look like the inn.
    ///
    /// Resting to an hour still ahead of now is a same-day rest and passes through unchanged. Resting to
    /// the hour it already is means a full day round — an inn is not a no-op.
    /// </summary>
    public static float RestTarget(float currentTimeOfDay, int restHour)
    {
        float now = (float)(currentTimeOfDay - (Math.Floor(currentTimeOfDay / 24d) * 24d));
        float target = Math.Clamp(restHour, 0, 23);

        return target > now ? target : target + 24f;
    }

    /// <summary>How many whole hours <see cref="RestTarget"/> will pass — for the prompt, so the player
    /// knows whether they are buying a nap or a night.</summary>
    public static int RestHours(float currentTimeOfDay, int restHour) =>
        (int)Math.Round(RestTarget(currentTimeOfDay, restHour) - currentTimeOfDay);
}
