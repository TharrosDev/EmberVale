using Embervale.Crafting;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the station gate behind every craftability check (<see cref="CraftingComponent.CanCraft"/>):
/// hand recipes craft anywhere, station recipes only at their exact station.
/// </summary>
public class CraftingStationTests
{
    [Theory]
    [InlineData(CraftingStationType.Hand)]
    [InlineData(CraftingStationType.Forge)]
    [InlineData(CraftingStationType.Workbench)]
    [InlineData(CraftingStationType.Alchemy)]
    [InlineData(CraftingStationType.Cooking)]
    public void HandRecipe_IsAcceptedAtEveryStation(CraftingStationType open)
    {
        Assert.True(CraftingComponent.StationAccepts(CraftingStationType.Hand, open));
    }

    [Fact]
    public void StationRecipe_IsAcceptedOnlyAtItsExactStation()
    {
        Assert.True(CraftingComponent.StationAccepts(CraftingStationType.Forge, CraftingStationType.Forge));
    }

    [Theory]
    [InlineData(CraftingStationType.Workbench)]
    [InlineData(CraftingStationType.Alchemy)]
    [InlineData(CraftingStationType.Cooking)]
    [InlineData(CraftingStationType.Hand)] // standing at no station can't run a forge recipe
    public void ForgeRecipe_IsRejectedElsewhere(CraftingStationType open)
    {
        Assert.False(CraftingComponent.StationAccepts(CraftingStationType.Forge, open));
    }
}
