using Embervale.Enemies;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the placement and pacing behind a boss's add waves (Phase 36D). The spawning runs in-engine,
/// but "where does each add land" and "how many may arrive" are pure — and both fail quietly: adds
/// stacked on one spot shove each other out of the fight, and a cap that does not hold turns a long
/// phase into an unwinnable screen.
/// </summary>
public class BossAddsTests
{
    private const float Radius = 4.5f;

    [Fact]
    public void ASingleAddStandsOffTheBossRatherThanInside()
    {
        Vector3 slot = BossAdds.SpawnSlot(0, 1, Radius);

        Assert.Equal(Radius, slot.Length(), 4);
        Assert.Equal(0f, slot.Y, 5);
    }

    [Fact]
    public void EveryAddLandsOnTheRing()
    {
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(Radius, BossAdds.SpawnSlot(i, 6, Radius).Length(), 4);
        }
    }

    [Fact]
    public void AddsAreSpreadRatherThanStacked()
    {
        // The failure this prevents: a wave arriving on one spot, shoving itself (and the boss)
        // out of the fight.
        Vector3 a = BossAdds.SpawnSlot(0, 4, Radius);
        Vector3 b = BossAdds.SpawnSlot(1, 4, Radius);
        Vector3 c = BossAdds.SpawnSlot(2, 4, Radius);

        Assert.True(a.DistanceTo(b) > 1f);
        Assert.True(b.DistanceTo(c) > 1f);
        Assert.True(a.DistanceTo(c) > 1f);
    }

    [Fact]
    public void TheSlotStaysOnTheGroundPlane()
    {
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(0f, BossAdds.SpawnSlot(i, 5, Radius).Y, 5);
        }
    }

    [Fact]
    public void ANegativeIndexIsTreatedAsTheFirstSlot()
    {
        Assert.Equal(BossAdds.SpawnSlot(0, 4, Radius), BossAdds.SpawnSlot(-2, 4, Radius));
    }

    // --- Wave pacing --------------------------------------------------------

    [Fact]
    public void AnUncappedWaveArrivesInFull()
    {
        Assert.Equal(3, BossAdds.SummonCount(waveCount: 3, alive: 0, maxAlive: 0));
        Assert.Equal(3, BossAdds.SummonCount(waveCount: 3, alive: 99, maxAlive: 0));
    }

    [Fact]
    public void ACappedWaveTopsUpToTheCap()
    {
        // A repeat should refill the fight, not stack on it.
        Assert.Equal(3, BossAdds.SummonCount(waveCount: 3, alive: 0, maxAlive: 3));
        Assert.Equal(1, BossAdds.SummonCount(waveCount: 3, alive: 2, maxAlive: 3));
    }

    [Fact]
    public void AFullCapSummonsNothing()
    {
        Assert.Equal(0, BossAdds.SummonCount(waveCount: 2, alive: 3, maxAlive: 3));
    }

    [Fact]
    public void TheCountNeverGoesNegativeWhenOverTheCap()
    {
        // Adds can outlive their cap if a phase re-entered or content changed under a save.
        Assert.Equal(0, BossAdds.SummonCount(waveCount: 2, alive: 10, maxAlive: 3));
    }

    [Fact]
    public void ANonsenseWaveSummonsNothingRatherThanThrowing()
    {
        Assert.Equal(0, BossAdds.SummonCount(waveCount: -4, alive: 0, maxAlive: 0));
        Assert.Equal(0, BossAdds.SummonCount(waveCount: 0, alive: -3, maxAlive: 2));
    }
}
