using System;

namespace Embervale.Enemies;

/// <summary>
/// Pure wave bookkeeping behind <see cref="SetPieceSpawnComponent"/>: when waves are released, how many
/// spawns are still owed, how many are alive, and when the whole set piece counts as cleared. Godot-free
/// so the timing is unit-tested; the component only turns "owed" into real placed enemies.
///
/// <para>Waves are released on a clock: the first at <see cref="Start"/>, then one every
/// <c>waveDelay</c> seconds until <c>waves</c> have been released, whether or not the previous wave is
/// dead (the point of a set piece is pressure, so it does not wait to be beaten). A spawn that could
/// not be placed stays owed and is offered again on the next <see cref="Tick"/>.</para>
/// </summary>
public sealed class SetPieceWaves
{
    private readonly float _waveDelay;
    private float _sinceWave;

    public SetPieceWaves(int waves, int countPerWave, float waveDelaySeconds)
    {
        Waves = Math.Max(1, waves);
        CountPerWave = Math.Max(1, countPerWave);
        _waveDelay = Math.Max(0f, waveDelaySeconds);
    }

    public int Waves { get; }

    public int CountPerWave { get; }

    public bool Started { get; private set; }

    /// <summary>Waves released so far (1 right after <see cref="Start"/>).</summary>
    public int WavesReleased { get; private set; }

    /// <summary>Spawns released but not yet placed.</summary>
    public int Owed { get; private set; }

    /// <summary>Spawned enemies not yet dead or gone.</summary>
    public int Alive { get; private set; }

    /// <summary>Total spawned (placed) so far.</summary>
    public int TotalSpawned { get; private set; }

    /// <summary>Every wave released, every spawn placed, and nothing left alive.</summary>
    public bool Cleared => Started && WavesReleased >= Waves && Owed == 0 && Alive == 0;

    /// <summary>Whether a point (in the trigger node's local space) lies inside a box of the given size
    /// centred on the node's origin.</summary>
    public static bool InsideBox(float x, float y, float z, float sizeX, float sizeY, float sizeZ) =>
        Math.Abs(x) <= sizeX * 0.5f && Math.Abs(y) <= sizeY * 0.5f && Math.Abs(z) <= sizeZ * 0.5f;

    /// <summary>Begins the set piece and releases the first wave. A second call does nothing.</summary>
    public void Start()
    {
        if (Started)
        {
            return;
        }

        Started = true;
        Release();
    }

    /// <summary>Advances the wave clock; returns how many spawns are owed and should be attempted now.</summary>
    public int Tick(float deltaSeconds)
    {
        if (!Started)
        {
            return 0;
        }

        if (WavesReleased < Waves)
        {
            _sinceWave += Math.Max(0f, deltaSeconds);
            if (_sinceWave >= _waveDelay)
            {
                Release();
            }
        }

        return Owed;
    }

    /// <summary>Records that <paramref name="count"/> owed spawns were placed.</summary>
    public void Spawned(int count)
    {
        int placed = Math.Clamp(count, 0, Owed);
        Owed -= placed;
        Alive += placed;
        TotalSpawned += placed;
    }

    /// <summary>Records one spawned enemy dying or leaving the world.</summary>
    public void Died() => Alive = Math.Max(0, Alive - 1);

    private void Release()
    {
        WavesReleased++;
        Owed += CountPerWave;
        _sinceWave = 0f;
    }
}
