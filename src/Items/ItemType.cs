using Godot;

namespace Embervale.Items;

/// <summary>Broad category of an item, used for filtering, equipping and UI.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ItemType
{
    Misc,
    Consumable,
    Weapon,
    Armor,
    Material,
    Quest,
}

/// <summary>
/// Rarity tier. Drives UI colour now and the procedural loot tiers of Phase 7
/// (higher rarity = more/stronger affixes).
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

/// <summary>
/// Presentation helpers for <see cref="ItemRarity"/>. This is the **single authority** for the
/// rarity ramp — <c>UiTheme.RarityColor</c> delegates here rather than keeping a second copy,
/// and the world-space users (<c>ItemPickupFactory</c>'s drop glow, <c>TrophyStandComponent</c>'s
/// display tint) read the same values, so a dropped Epic and its inventory row can never
/// disagree. It lives here rather than in <c>UiTheme</c> only to keep <c>src/Items</c> from
/// taking a dependency on <c>src/UI</c>.
/// </summary>
public static class ItemRarities
{
    /// <summary>
    /// The rarity ramp, retuned to the world's palette in Phase 37.5A. The pre-37.5 values were
    /// stock MMO rarity colours (fully saturated green/blue/purple/orange) which broke
    /// <c>docs/UI_STYLE.md</c> §2 — in a UI where "only accents may exceed ~40% saturation" they
    /// were the loudest thing on screen, and they read as a different game's chrome dropped into
    /// an ash-and-ember world.
    ///
    /// Three properties are deliberate, are pinned by <c>RarityRampTests</c>, and must survive any
    /// retune:
    /// 1. **Luminance climbs strictly with rarity.** A rarer item is a brighter one, so the ramp
    ///    still orders correctly in greyscale, in a screenshot, and to a colourblind player whose
    ///    hue discrimination the ramp cannot rely on. This is the constraint that forced the ramp
    ///    paler than a stock one: hue carries the *flavour*, luminance carries the *rank*.
    /// 2. **Adjacent tiers stay separated** (≥1.15:1). Monotonic is not enough — steps too small
    ///    to see are the same as no ordering at all.
    /// 3. **Legendary out-burns <c>UiTheme.Accent</c>.** Ember gold is the UI's accent, so a
    ///    Legendary that merely matched it would not read as an event. Affordable precisely
    ///    because Legendary is rare.
    ///
    /// Colour is still never the *only* channel: <c>UiTheme.RarityBorderWidth</c> thickens the slot
    /// frame at Epic and above, and rarity is always available as a word in the tooltip.
    /// </summary>
    public static Color Color(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => new Color(0.60f, 0.58f, 0.52f),      // ash bone — near Dim, and meant to be
            ItemRarity.Uncommon => new Color(0.52f, 0.70f, 0.47f),    // sage
            ItemRarity.Rare => new Color(0.56f, 0.72f, 0.90f),        // cold steel
            ItemRarity.Epic => new Color(0.84f, 0.72f, 0.95f),        // aged amethyst
            ItemRarity.Legendary => new Color(0.99f, 0.86f, 0.55f),   // white-hot ember
            _ => Colors.White,
        };
    }
}
