namespace Embervale.Crafting;

/// <summary>
/// The kind of station a recipe is crafted at. A recipe declares the station it needs;
/// a <see cref="CraftingStationComponent"/> in the world advertises a type, and the
/// crafting UI shows the recipes that match (plus <see cref="Hand"/> recipes, which can
/// be made anywhere). New station = a new enum value + a station in the scene.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum CraftingStationType
{
    /// <summary>No station needed — craftable at any station.</summary>
    Hand,
    Forge,
    Workbench,
    Alchemy,
    Cooking,
}

/// <summary>Display helpers for <see cref="CraftingStationType"/>.</summary>
public static class CraftingStations
{
    public static string Label(CraftingStationType station) => station switch
    {
        CraftingStationType.Forge => "Forge",
        CraftingStationType.Workbench => "Workbench",
        CraftingStationType.Alchemy => "Alchemy Table",
        CraftingStationType.Cooking => "Cooking Fire",
        _ => "Hand",
    };
}
