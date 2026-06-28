using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Crafting;

/// <summary>Raised when a player interacts with a crafting station (opens the UI).</summary>
public readonly record struct CraftingStationOpenedEvent(IEntity Player, CraftingStationType Station, string StationName) : IGameEvent;

/// <summary>Raised when the crafting UI is dismissed.</summary>
public readonly record struct CraftingStationClosedEvent(IEntity Player) : IGameEvent;

/// <summary>Raised after an item is successfully crafted (ingredients consumed, output added).</summary>
public readonly record struct ItemCraftedEvent(IEntity Crafter, string RecipeId, string OutputItemId, int Quantity) : IGameEvent;

/// <summary>Raised after an item is deconstructed/salvaged (item consumed, materials + XP returned).</summary>
public readonly record struct ItemDeconstructedEvent(IEntity Crafter, string ItemId, int XpAwarded) : IGameEvent;

/// <summary>Raised when a new recipe is learned.</summary>
public readonly record struct RecipeLearnedEvent(IEntity Crafter, string RecipeId) : IGameEvent;
