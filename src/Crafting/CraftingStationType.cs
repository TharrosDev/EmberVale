namespace Embervale.Crafting;

/// <summary>
/// The kind of station a recipe is crafted at. A recipe declares the station it needs;
/// a <see cref="CraftingStationComponent"/> in the world advertises a type, and the
/// crafting UI shows the recipes that match (plus <see cref="Hand"/> recipes, which can
/// be made anywhere). New station = a new enum value + a station in the scene.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
// ⚠️ Ordinal 4 was Cooking, retired in Phase 40 having never been authored against: zero recipes,
// zero stations, zero scenes. Survival needs (hunger/food/durability/temperature) are CUT, not
// deferred (maintainer direction, 2026-08-12), so a cooking fire has nothing left to be for, and
// "a cut system leaves no stub" is the rule Phase 40 itself named. Removing the LAST member shifts
// no other ordinal, which is the only reason this deletion was safe — do not take it as licence to
// remove a member with authored data behind it. 4 stays retired; the next station appends at 5.
public enum CraftingStationType
{
    /// <summary>No station needed — craftable at any station.</summary>
    Hand,
    Forge,
    Workbench,
    Alchemy,
}

/// <summary>Display helpers for <see cref="CraftingStationType"/>.</summary>
public static class CraftingStations
{
    public static string Label(CraftingStationType station) => station switch
    {
        CraftingStationType.Forge => "Forge",
        CraftingStationType.Workbench => "Workbench",
        CraftingStationType.Alchemy => "Alchemy Table",
        _ => "Hand",
    };
}
