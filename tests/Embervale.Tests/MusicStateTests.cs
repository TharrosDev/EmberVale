using Embervale.Audio;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the adaptive-music priority table (Phase 31B): boss &gt; combat &gt; safe &gt; explore. A wrong
/// priority would, e.g., drop from boss music to a safe-zone bed mid-fight, so it is pinned here. The
/// resolver is pure (Godot-free); the MusicDirector's event/crossfade wiring is exercised in-engine.
/// </summary>
public class MusicStateTests
{
    [Fact]
    public void Default_IsExplore() =>
        Assert.Equal(MusicState.Explore, new MusicStateMachine().Resolve());

    [Fact]
    public void SafeZone_WhenNothingEngaged()
    {
        var m = new MusicStateMachine { InSafeZone = true };
        Assert.Equal(MusicState.Safe, m.Resolve());
    }

    [Fact]
    public void Combat_WhenEnemiesEngaged()
    {
        var m = new MusicStateMachine { Combatants = 2 };
        Assert.Equal(MusicState.Combat, m.Resolve());
    }

    [Fact]
    public void Combat_OverridesSafeZone()
    {
        var m = new MusicStateMachine { InSafeZone = true, Combatants = 1 };
        Assert.Equal(MusicState.Combat, m.Resolve());
    }

    [Fact]
    public void Boss_OverridesCombat()
    {
        var m = new MusicStateMachine { BossActive = true, Combatants = 3, InSafeZone = true };
        Assert.Equal(MusicState.Boss, m.Resolve());
    }

    [Fact]
    public void ClearingBoss_FallsBackToCombatThenExplore()
    {
        var m = new MusicStateMachine { BossActive = true, Combatants = 1 };
        Assert.Equal(MusicState.Boss, m.Resolve());

        m.BossActive = false;
        Assert.Equal(MusicState.Combat, m.Resolve());

        m.Combatants = 0;
        Assert.Equal(MusicState.Explore, m.Resolve());
    }
}
