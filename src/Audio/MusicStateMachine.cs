namespace Embervale.Audio;

/// <summary>The adaptive-music states (Phase 31B), in ascending priority.</summary>
public enum MusicState
{
    /// <summary>Default wandering bed.</summary>
    Explore,

    /// <summary>Inside a region's safe zone (town/hub) with nothing hostile engaged.</summary>
    Safe,

    /// <summary>At least one enemy is actively engaging the player.</summary>
    Combat,

    /// <summary>A boss encounter is live — overrides ordinary combat.</summary>
    Boss,
}

/// <summary>
/// Pure resolver for the adaptive-music state (Phase 31B): boss overrides combat, combat overrides a
/// safe zone, safe overrides plain exploration. Godot-free so the transition table unit-tests under
/// <c>dotnet test</c>; the <see cref="MusicDirector"/> feeds it from EventBus signals and crossfades on
/// the resolved change.
/// </summary>
public sealed class MusicStateMachine
{
    /// <summary>A boss encounter is active (until the boss dies).</summary>
    public bool BossActive { get; set; }

    /// <summary>Count of enemies currently engaging the player.</summary>
    public int Combatants { get; set; }

    /// <summary>The player is inside a safe zone.</summary>
    public bool InSafeZone { get; set; }

    /// <summary>The state the music should currently be in, by priority.</summary>
    public MusicState Resolve()
    {
        if (BossActive)
        {
            return MusicState.Boss;
        }

        if (Combatants > 0)
        {
            return MusicState.Combat;
        }

        return InSafeZone ? MusicState.Safe : MusicState.Explore;
    }
}
