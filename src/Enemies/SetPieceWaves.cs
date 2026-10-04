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

    /// <summary>Authoring ranges of a set piece. An authored range fails silently at both ends (NOW.md
    /// invariant 8): zero waves is a piece that never does anything, a hundred is a frame-rate trap.</summary>
    public const int MaxWaves = 10;
    public const int MaxCountPerWave = 12;
    public const int MaxTotalSpawns = 60;
    public const float MaxWaveDelay = 600f;
    public const float MaxTriggerExtent = 200f;
    public const float MaxSpawnRadius = 60f;

    /// <summary>What is wrong with a set piece's numeric settings (empty when nothing is). Used by the
    /// content validator on scene-authored pieces and unit-tested in both directions.</summary>
    public static System.Collections.Generic.List<string> ConfigProblems(
        int waves, int countPerWave, float waveDelaySeconds, bool flagTriggered,
        float sizeX, float sizeY, float sizeZ, float spawnRadius, int templateCount)
    {
        var problems = new System.Collections.Generic.List<string>();
        if (waves < 1 || waves > MaxWaves)
        {
            problems.Add($"Waves {waves} is outside 1..{MaxWaves}");
        }

        if (countPerWave < 1 || countPerWave > MaxCountPerWave)
        {
            problems.Add($"CountPerWave {countPerWave} is outside 1..{MaxCountPerWave}");
        }

        if (waves >= 1 && countPerWave >= 1 && (long)waves * countPerWave > MaxTotalSpawns)
        {
            problems.Add($"{waves} wave(s) of {countPerWave} is more than {MaxTotalSpawns} spawns");
        }

        if (float.IsNaN(waveDelaySeconds) || waveDelaySeconds < 0f || waveDelaySeconds > MaxWaveDelay)
        {
            problems.Add($"WaveDelaySeconds {waveDelaySeconds} is outside 0..{MaxWaveDelay}");
        }

        if (!flagTriggered && !(sizeX > 0f && sizeY > 0f && sizeZ > 0f &&
                                sizeX <= MaxTriggerExtent && sizeY <= MaxTriggerExtent && sizeZ <= MaxTriggerExtent))
        {
            problems.Add($"TriggerSize ({sizeX}, {sizeY}, {sizeZ}) must be positive and at most {MaxTriggerExtent} on each axis");
        }

        if (float.IsNaN(spawnRadius) || spawnRadius < 1f || spawnRadius > MaxSpawnRadius)
        {
            problems.Add($"SpawnRadius {spawnRadius} is outside 1..{MaxSpawnRadius}");
        }

        if (templateCount < 1)
        {
            problems.Add("TemplateIds is empty");
        }

        return problems;
    }

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
