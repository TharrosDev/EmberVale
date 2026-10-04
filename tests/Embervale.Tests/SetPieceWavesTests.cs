using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>The pure wave bookkeeping behind SetPieceSpawnComponent.</summary>
public class SetPieceWavesTests
{
    [Fact]
    public void NothingIsOwedUntilStarted()
    {
        var waves = new SetPieceWaves(waves: 2, countPerWave: 3, waveDelaySeconds: 10f);

        Assert.Equal(0, waves.Tick(5f));
        Assert.False(waves.Started);
        Assert.False(waves.Cleared);
    }

    [Fact]
    public void StartReleasesTheFirstWaveAtOnce()
    {
        var waves = new SetPieceWaves(2, 3, 10f);

        waves.Start();

        Assert.True(waves.Started);
        Assert.Equal(1, waves.WavesReleased);
        Assert.Equal(3, waves.Tick(0f));
    }

    [Fact]
    public void LaterWavesReleaseOnTheClockWhetherOrNotTheLastWaveIsDead()
    {
        var waves = new SetPieceWaves(3, 2, 10f);
        waves.Start();
        waves.Spawned(2);

        Assert.Equal(0, waves.Tick(9.9f));
        Assert.Equal(2, waves.Tick(0.2f)); // wave 2 released with wave 1 still alive
        Assert.Equal(2, waves.WavesReleased);
        waves.Spawned(2);
        Assert.Equal(4, waves.Alive);

        Assert.Equal(2, waves.Tick(10f));
        Assert.Equal(3, waves.WavesReleased);
        Assert.Equal(2, waves.Tick(100f)); // no fourth wave
        Assert.Equal(3, waves.WavesReleased);
    }

    [Fact]
    public void AnUnplacedSpawnStaysOwedAndIsOfferedAgain()
    {
        var waves = new SetPieceWaves(1, 4, 10f);
        waves.Start();

        Assert.Equal(4, waves.Tick(1f));
        waves.Spawned(1);

        Assert.Equal(3, waves.Owed);
        Assert.Equal(3, waves.Tick(1f));
        waves.Spawned(3);
        Assert.Equal(0, waves.Owed);
        Assert.Equal(4, waves.TotalSpawned);
    }

    [Fact]
    public void SpawnedCannotExceedWhatIsOwed()
    {
        var waves = new SetPieceWaves(1, 2, 0f);
        waves.Start();

        waves.Spawned(10);

        Assert.Equal(2, waves.Alive);
        Assert.Equal(0, waves.Owed);
    }

    [Fact]
    public void ClearedNeedsEveryWaveReleasedPlacedAndKilled()
    {
        var waves = new SetPieceWaves(2, 2, 5f);
        waves.Start();
        waves.Spawned(2);
        waves.Died();
        waves.Died();

        Assert.False(waves.Cleared); // the second wave has not been released yet

        waves.Tick(5f);
        Assert.False(waves.Cleared); // released but owed
        waves.Spawned(2);
        waves.Died();
        Assert.False(waves.Cleared);
        waves.Died();

        Assert.True(waves.Cleared);
    }

    [Fact]
    public void DeathsNeverGoNegative()
    {
        var waves = new SetPieceWaves(1, 1, 0f);
        waves.Start();
        waves.Spawned(1);

        waves.Died();
        waves.Died();

        Assert.Equal(0, waves.Alive);
        Assert.True(waves.Cleared);
    }

    [Fact]
    public void StartingTwiceDoesNotReleaseAnotherWave()
    {
        var waves = new SetPieceWaves(2, 3, 10f);

        waves.Start();
        waves.Start();

        Assert.Equal(1, waves.WavesReleased);
        Assert.Equal(3, waves.Owed);
    }

    [Fact]
    public void BadSettingsAreClampedToOneWaveOfOne()
    {
        var waves = new SetPieceWaves(0, 0, -5f);
        waves.Start();

        Assert.Equal(1, waves.Waves);
        Assert.Equal(1, waves.CountPerWave);
        Assert.Equal(1, waves.Owed);
    }

    [Fact]
    public void ARestartIsAFreshInstance()
    {
        var first = new SetPieceWaves(1, 2, 0f);
        first.Start();
        first.Spawned(1);

        var restart = new SetPieceWaves(1, 2, 0f);
        restart.Start();

        Assert.Equal(2, restart.Owed);
        Assert.Equal(0, restart.Alive);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, true)]
    [InlineData(11.9f, 3.9f, -11.9f, true)]
    [InlineData(12.1f, 0f, 0f, false)]
    [InlineData(0f, 4.1f, 0f, false)]
    [InlineData(0f, -4.1f, 0f, false)]
    [InlineData(0f, 0f, -12.1f, false)]
    public void TriggerBoxIsCentredOnTheNode(float x, float y, float z, bool inside)
    {
        Assert.Equal(inside, SetPieceWaves.InsideBox(x, y, z, 24f, 8f, 24f));
    }
}
