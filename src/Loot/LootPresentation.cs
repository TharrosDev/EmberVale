using Embervale.Items;

namespace Embervale.Loot;

/// <summary>
/// How a drop announces its rarity: the height of the light beam over the pickup and the pitch and
/// volume of its chime. Pure numbers, so the factory and the sound hook cannot disagree about which
/// rarities are loud. The chime reuses the existing <c>sfx.pickup</c> cue at a per-rarity pitch;
/// there are no per-rarity audio files.
/// </summary>
public static class LootPresentation
{
    /// <summary>The cue every loot chime plays, pitched by <see cref="ChimePitch"/>.</summary>
    public const string ChimeCue = "sfx.pickup";

    /// <summary>The lowest rarity that gets a beam and a chime; below it a drop is quiet.</summary>
    public const ItemRarity FirstAnnounced = ItemRarity.Rare;

    public static bool IsAnnounced(ItemRarity rarity) => rarity >= FirstAnnounced;

    /// <summary>Beam height in metres; 0 means no beam.</summary>
    public static float BeamHeight(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => 1.2f,
            ItemRarity.Rare => 2.4f,
            ItemRarity.Epic => 3.6f,
            ItemRarity.Legendary => 5.5f,
            _ => 0f,
        };
    }

    /// <summary>Playback pitch of the chime: higher rarity rings higher.</summary>
    public static float ChimePitch(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => 1.1f,
            ItemRarity.Rare => 1.25f,
            ItemRarity.Epic => 1.45f,
            ItemRarity.Legendary => 1.7f,
            _ => 1f,
        };
    }

    /// <summary>Volume offset of the chime in decibels: higher rarity rings louder.</summary>
    public static float ChimeVolumeDb(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Epic => 2f,
            ItemRarity.Legendary => 4f,
            _ => 0f,
        };
    }
}
