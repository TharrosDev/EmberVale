using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure persistence policy behind Phase 25.5A. The registration/SaveManager wiring runs
/// against Godot (verified in-engine via the <c>savecheck</c> dev command), but the load-bearing
/// decisions — whether an actor persists, the key it builds, and whether a key is volatile (would
/// orphan on reload) — are pure and pinned here.
/// </summary>
public class SaveKeyPolicyTests
{
    [Theory]
    [InlineData("player", true)]
    [InlineData("ember_crown.waystone.relic", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldPersist_TracksStableId(string? persistentId, bool expected)
    {
        Assert.Equal(expected, SaveKeyPolicy.ShouldPersist(persistentId));
    }

    [Fact]
    public void Key_UsesPersistentId_WhenPresent()
    {
        Assert.Equal("stats:player", SaveKeyPolicy.Key("stats", "player", 42uL));
    }

    [Fact]
    public void Key_FallsBackToRuntimeId_WhenTransient()
    {
        Assert.Equal("stats:42", SaveKeyPolicy.Key("stats", "", 42uL));
        Assert.Equal("stats:42", SaveKeyPolicy.Key("stats", null, 42uL));
    }

    [Theory]
    [InlineData("stats:42", true)]   // runtime id → volatile, orphans on reload
    [InlineData("stats:3", true)]
    [InlineData("stats:player", false)] // stable id
    [InlineData("inventory:ember_crown.waystone.relic", false)]
    [InlineData("map", false)]          // world-service id, no colon
    [InlineData("fasttravel", false)]
    [InlineData("", false)]
    [InlineData("stats:", false)]       // empty suffix is not a runtime id
    public void IsVolatile_FlagsRuntimeKeyedIds(string saveId, bool expected)
    {
        Assert.Equal(expected, SaveKeyPolicy.IsVolatile(saveId));
    }
}
