using Embervale.World;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the no-spawn areas (Phase 38K, which turned the single region bubble into a list). The failure
/// modes here are both silent and both the wrong way round: a district that stops protecting itself
/// drops goblins among the merchants, and a stale zone left over from a region transition makes enemies
/// quietly refuse to spawn on ground with nothing on it.
///
/// <see cref="SafeZones"/> is static process state, so every test sets it up from scratch — which is
/// also the property being pinned in <see cref="SetReplacesRatherThanAccumulates"/>.
/// </summary>
public class SafeZoneTests
{
    [Fact]
    public void ASingleZoneStillWorksExactlyAsItDid()
    {
        SafeZones.Set(new Vector3(0, 0, -10), 34f);

        Assert.True(SafeZones.Contains(new Vector3(0, 0, -10)));
        Assert.True(SafeZones.Contains(new Vector3(0, 0, 20)));    // 30 out, inside
        Assert.False(SafeZones.Contains(new Vector3(0, 0, 30)));   // 40 out, beyond
        Assert.False(SafeZones.Contains(new Vector3(60, 0, -10)));
    }

    [Fact]
    public void ASecondDistrictProtectsItselfWithoutWideningTheFirst()
    {
        // The Ember Crown's actual numbers: the square's bubble, plus the Embermarket's own.
        SafeZones.Set(new Vector3(0, 0, -10), 34f);
        SafeZones.Add(new Vector3(0, 0, 46), 32f);

        Assert.True(SafeZones.Contains(new Vector3(0, 0, 46)));    // the market square
        Assert.True(SafeZones.Contains(new Vector3(0, 0, -10)));   // the town square

        // ⚠️ The strip between them must be covered by one or the other, or a warband spawns on the road
        // between two safe places — which reads as the safe zone being broken rather than as a frontier.
        for (int z = -10; z <= 46; z++)
        {
            Assert.True(
                SafeZones.Contains(new Vector3(0, 0, z)),
                $"the road between the square and the market is unprotected at z={z}");
        }

        // And the wilds still are not safe, which is the whole reason this is a list rather than one
        // bubble stretched to reach the market.
        Assert.False(SafeZones.Contains(new Vector3(0, 0, -65)));  // wilds north
        Assert.False(SafeZones.Contains(new Vector3(-55, 0, -10))); // wilds west
    }

    [Fact]
    public void SetReplacesRatherThanAccumulates()
    {
        SafeZones.Set(new Vector3(0, 0, -10), 34f);
        SafeZones.Add(new Vector3(0, 0, 46), 32f);

        // A region transition calls Set first. If it merged, the previous realm's districts would keep
        // protecting empty ground here — the same replace-never-merge rule ISaveable.Load follows.
        SafeZones.Set(new Vector3(500, 0, 500), 20f);

        Assert.False(SafeZones.Contains(new Vector3(0, 0, 46)));
        Assert.False(SafeZones.Contains(new Vector3(0, 0, -10)));
        Assert.True(SafeZones.Contains(new Vector3(500, 0, 500)));
    }

    [Fact]
    public void ANonPositiveRadiusIsNotAZone()
    {
        // RegionCellResource.SafeRadius defaults to 0, which is how every cell authored before 38K says
        // "not a safe area". If a 0 stored a zone, the point at its exact centre would be safe.
        SafeZones.Clear();
        SafeZones.Add(new Vector3(0, 0, 46), 0f);
        SafeZones.Add(new Vector3(0, 0, 46), -5f);

        Assert.False(SafeZones.Contains(new Vector3(0, 0, 46)));
    }

    [Fact]
    public void NoZonesMeansNowhereIsSafe()
    {
        SafeZones.Clear();

        Assert.False(SafeZones.Contains(Vector3.Zero));
        Assert.False(SafeZones.Contains(new Vector3(0, 0, 46)));
    }

    // ⚠️ There is deliberately no test for TryRingPointOutside, and the reason is worth the comment:
    // it calls GD.Randf(), which needs the engine. Calling it from the test project does not fail — it
    // ABORTS THE WHOLE RUN, after the passing tests have already printed. What it does on top of
    // Contains is one rejection loop, and Contains is pinned above; if that loop ever needs pinning,
    // the fix is to pass the angle in rather than to host Godot in the test project.
}
