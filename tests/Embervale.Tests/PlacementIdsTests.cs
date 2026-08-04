using Embervale.Housing;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="PlacementIds"/> (Phase 37C). The load-bearing test is
/// <see cref="TheIndexIsDerivedFromExistingIdsNotACounter"/>: a counter would reset on load and
/// hand back an id that is already spawned, and <c>PersistentSpawnDirector.Spawn</c> answers a known
/// id by returning the existing actor — so the new prop would silently never appear, and only in a
/// session that had loaded a save.
/// </summary>
public class PlacementIdsTests
{
    private const string Cottage = "property.ember_crown.cottage";

    [Fact]
    public void TheFirstPropInAHoldingIsIndexOne()
    {
        Assert.Equal($"place.{Cottage}#1", PlacementIds.Next(System.Array.Empty<string>(), Cottage));
    }

    [Fact]
    public void TheIndexIsDerivedFromExistingIdsNotACounter()
    {
        string[] existing =
        {
            $"place.{Cottage}#1",
            $"place.{Cottage}#2",
            $"place.{Cottage}#7",
        };

        // One past the highest, not one past the count — #3 would collide the moment #7 exists,
        // which is exactly what happens after props are removed and others added.
        Assert.Equal($"place.{Cottage}#8", PlacementIds.Next(existing, Cottage));
    }

    [Fact]
    public void OtherHoldingsAndOtherActorsDoNotShiftTheIndex()
    {
        string[] existing =
        {
            $"place.property.somewhere.else#40",
            "cache.world.start",
            "prop.cache#12",
            $"place.{Cottage}#2",
        };

        Assert.Equal($"place.{Cottage}#3", PlacementIds.Next(existing, Cottage));
    }

    [Fact]
    public void AMalformedIdCannotStopPlacementWorking()
    {
        string[] existing =
        {
            $"place.{Cottage}#",
            $"place.{Cottage}#abc",
            $"place.{Cottage}#-4",
            $"place.{Cottage}#3",
        };

        Assert.Equal($"place.{Cottage}#4", PlacementIds.Next(existing, Cottage));
    }

    [Fact]
    public void OnlyPlacementIdsAreRecognisedAsPlacements()
    {
        Assert.True(PlacementIds.IsPlacement($"place.{Cottage}#1"));
        Assert.False(PlacementIds.IsPlacement("cache.world.start"));
        Assert.False(PlacementIds.IsPlacement(string.Empty));
        Assert.False(PlacementIds.IsPlacement(null));
    }

    [Fact]
    public void ThePropertyIsRecoverableFromTheId()
    {
        // This is the whole reason the id carries the property: 37D asks a holding what stands in it
        // without a second save record to keep in step.
        Assert.Equal(Cottage, PlacementIds.PropertyOf($"place.{Cottage}#5"));
        Assert.Equal(string.Empty, PlacementIds.PropertyOf("prop.cache#1"));
        Assert.Equal(string.Empty, PlacementIds.PropertyOf("place.#1"));
    }
}
