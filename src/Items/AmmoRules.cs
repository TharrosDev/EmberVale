using System;

namespace Embervale.Items;

/// <summary>
/// What an arrow is worth, free of the engine so it is unit-testable. A bow's damage is its own; the
/// arrow adds a flat amount by its <see cref="ItemResource.Tier"/>, so better arrows are worth buying
/// without ever out-scaling the bow that looses them.
/// </summary>
public static class AmmoRules
{
    /// <summary>Toast shown when the player looses a bow with an empty ammo slot.</summary>
    public const string NoAmmoReasonKey = "item.ammo.refused.none";

    /// <summary>Flat damage each tier above the first adds to a shot.</summary>
    public const float DamagePerTier = 4f;

    /// <summary>
    /// Flat damage added to the bow's base before the shot is rolled. Tier 0 (a legacy arrow with no
    /// tier) and tier 1 (plain arrows) add nothing; each tier above adds <see cref="DamagePerTier"/>.
    /// </summary>
    public static float BonusDamage(int tier) => Math.Max(0, tier - 1) * DamagePerTier;
}
