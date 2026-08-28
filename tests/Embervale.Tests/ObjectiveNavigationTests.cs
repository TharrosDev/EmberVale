using Embervale.Quests;
using Xunit;

namespace Embervale.Tests;

public class ObjectiveNavigationTests
{
    [Theory]
    [InlineData(ObjectiveType.Reach)]
    [InlineData(ObjectiveType.Defend)]
    public void GeographicObjective_UsesTargetAsCanonicalLocation(ObjectiveType type)
    {
        Assert.Equal(
            "location.hollowreach.reach",
            ObjectiveNavigation.LocationId(type, "location.hollowreach.reach", string.Empty));
    }

    [Theory]
    [InlineData(ObjectiveType.Kill)]
    [InlineData(ObjectiveType.Collect)]
    [InlineData(ObjectiveType.Talk)]
    [InlineData(ObjectiveType.Escort)]
    [InlineData(ObjectiveType.Interact)]
    public void LiveOrInteractionObjective_UsesAuthoredFallback(ObjectiveType type)
    {
        Assert.Equal(
            "location.frostfang.ash_roost",
            ObjectiveNavigation.LocationId(
                type, "enemy.ash_dragon", "location.frostfang.ash_roost"));
    }

    [Fact]
    public void ObjectiveWithoutGeographicDestination_ReturnsEmpty()
    {
        Assert.Empty(ObjectiveNavigation.LocationId(ObjectiveType.Kill, "enemy.goblin", null));
    }
}
