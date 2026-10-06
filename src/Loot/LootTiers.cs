using System;
using Embervale.World;

namespace Embervale.Loot;

/// <summary>
/// The six realm tiers as loot sees them: which tier a realm drops, the level band a tier's gear is
/// generated in, and the item level one drop rolls at. Pure (no Godot), so every rule is unit-tested.
/// The numbers mirror <c>TIER_LEVELS</c> / <c>TIER_REGION</c> / <c>TIER_ITEM_LEVEL</c> in
/// <c>tools/items/catalogue.py</c>, which is where the item catalogue gets the same bands.
/// </summary>
public static class LootTiers
{
    public const int MinTier = 1;
    public const int MaxTier = 6;

    /// <summary>The token a nested table path carries where the tier number goes
    /// (<c>res://data/loot/tiers/Tier{tier}Gear.tres</c>).</summary>
    public const string TierToken = "{tier}";

    /// <summary>How far above or below the finder's level one drop may roll.</summary>
    public const int LevelSpread = 2;

    private static readonly (int Low, int High)[] Bands =
    {
        (1, 10), (8, 20), (18, 30), (28, 40), (36, 46), (44, 50),
    };

    private static readonly int[] Typical = { 5, 14, 24, 34, 41, 47 };

    public static bool IsTier(int tier) => tier >= MinTier && tier <= MaxTier;

    /// <summary>The tier a realm's enemies and chests drop. The celestial realm is the last tier even
    /// though its enum ordinal sits before the fifth realm's (ordinals are append-only).</summary>
    public static int TierOfRealm(Realm realm)
    {
        return realm switch
        {
            Realm.EmberCrown => 1,
            Realm.FrostfangReach => 2,
            Realm.AshenWilds => 3,
            Realm.SunspireDominion => 4,
            Realm.PaleConcord => 5,
            Realm.CelestialRealm => 6,
            _ => 1,
        };
    }

    /// <summary>The (lowest, highest) item level of a tier; an unknown tier answers (0, 0).</summary>
    public static (int Low, int High) LevelBand(int tier)
    {
        return IsTier(tier) ? Bands[tier - 1] : (0, 0);
    }

    /// <summary>The item level a tier's gear is authored at; 0 for an unknown tier.</summary>
    public static int TypicalItemLevel(int tier)
    {
        return IsTier(tier) ? Typical[tier - 1] : 0;
    }

    /// <summary>
    /// The item level one drop rolls at. With a known finder level the drop lands within
    /// <see cref="LevelSpread"/> of it, so loot stays relevant to whoever is looting; without one it
    /// is uniform across the band. Either way the result never leaves the tier's band, which is what
    /// stops an over-levelled player pulling late-game numbers out of the first realm.
    /// </summary>
    /// <param name="tier">1..6; anything else answers 0 (a level-less roll).</param>
    /// <param name="finderLevel">The looting character's level, or 0 when unknown.</param>
    /// <param name="roll01">A uniform sample in 0..1.</param>
    public static int RollItemLevel(int tier, int finderLevel, float roll01)
    {
        if (!IsTier(tier))
        {
            return 0;
        }

        (int low, int high) = Bands[tier - 1];
        roll01 = Math.Clamp(roll01, 0f, 1f);
        if (finderLevel <= 0)
        {
            return Math.Clamp(low + (int)MathF.Floor(roll01 * (high - low + 1)), low, high);
        }

        int offset = (int)MathF.Round((roll01 * 2f * LevelSpread) - LevelSpread);
        return Math.Clamp(finderLevel + offset, low, high);
    }

    /// <summary>The level an entry's <c>MinLevel</c> / <c>MaxLevel</c> gate is read against: the
    /// finder's level held inside the tier's band, or the tier's typical level with no finder.</summary>
    public static int ReferenceLevel(int tier, int finderLevel)
    {
        if (!IsTier(tier))
        {
            return Math.Max(0, finderLevel);
        }

        (int low, int high) = Bands[tier - 1];
        return finderLevel > 0 ? Math.Clamp(finderLevel, low, high) : Typical[tier - 1];
    }

    /// <summary>Holds a rolled level inside the band of the item's own tier, so a first-realm template
    /// dropped in a later realm is still a first-realm item. An untiered template (0) is not clamped.</summary>
    public static int ClampToItemTier(int itemLevel, int itemTier)
    {
        if (itemLevel <= 0 || !IsTier(itemTier))
        {
            return Math.Max(0, itemLevel);
        }

        (int low, int high) = Bands[itemTier - 1];
        return Math.Clamp(itemLevel, low, high);
    }

    /// <summary>Fills <see cref="TierToken"/> in a nested table path. A path without the token is
    /// returned unchanged; a tokened path with no usable tier falls back to the first tier.</summary>
    public static string ResolvePath(string path, int tier)
    {
        if (string.IsNullOrEmpty(path) || !path.Contains(TierToken, StringComparison.Ordinal))
        {
            return path ?? string.Empty;
        }

        int resolved = IsTier(tier) ? tier : MinTier;
        return path.Replace(TierToken, resolved.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}
