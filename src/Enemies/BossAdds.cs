using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The pure placement and pacing arithmetic behind a boss's add waves (Phase 36D), kept Godot-struct
/// only so both are unit-testable apart from the spawner — the same idiom as
/// <see cref="BossPhases"/>, <see cref="GuardCycle"/> and <see cref="PackFlank"/>.
/// </summary>
public static class BossAdds
{
    /// <summary>
    /// Offset from the boss for the add at <paramref name="index"/> of <paramref name="count"/>,
    /// spread evenly around a ring of <paramref name="radius"/> metres. Used only when the arena
    /// declares no <c>boss_add_spawn</c> markers — a lair has none, and adds arriving in a heap on
    /// top of each other shove one another (and the boss) out of the fight.
    ///
    /// The ring starts behind the boss's local -Z rather than at an axis, so a wave summoned into an
    /// arena the player is facing does not open with one add materialising inside them.
    /// </summary>
    public static Vector3 SpawnSlot(int index, int count, float radius)
    {
        if (count <= 1)
        {
            return new Vector3(0f, 0f, radius);
        }

        int i = index < 0 ? 0 : index;
        float step = Mathf.Tau / count;
        float angle = (i * step) + (Mathf.Pi * 0.5f);
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    /// <summary>
    /// How many of a wave to summon now, given how many of its adds are still alive. Honours the
    /// wave's cap so a repeating wave tops the fight up rather than stacking on it — an uncapped
    /// repeat ends a fight by burying the player, which is why the validator rejects one.
    /// A cap of <c>0</c> means uncapped, which is only legal for a one-shot wave.
    /// </summary>
    public static int SummonCount(int waveCount, int alive, int maxAlive)
    {
        int wanted = waveCount < 0 ? 0 : waveCount;
        if (maxAlive <= 0)
        {
            return wanted;
        }

        int room = maxAlive - (alive < 0 ? 0 : alive);
        return room <= 0 ? 0 : Mathf.Min(wanted, room);
    }
}
