using Embervale.Entities;

namespace Embervale.Magic;

/// <summary>
/// The runtime instance of an active <see cref="StatusEffectResource"/> on one
/// entity: the authored definition plus its remaining lifetime and DoT tick timer.
/// Each instance is also the <c>Source</c> object for the stat modifier it applies,
/// so the modifier can be stripped cleanly when the effect ends.
/// </summary>
public sealed class StatusEffect
{
    public StatusEffect(StatusEffectResource definition, IEntity? source, float durationMultiplier = 1f)
    {
        Definition = definition;
        Source = source;
        DurationMultiplier = StatusMath.DurationMultiplier(durationMultiplier);
        Remaining = definition.Duration * DurationMultiplier;
        TickTimer = definition.TickInterval;
    }

    public StatusEffectResource Definition { get; }

    /// <summary>The caster that applied this effect; DoT kills are credited to it.</summary>
    public IEntity? Source { get; }

    public double Remaining { get; set; }

    public double TickTimer { get; set; }

    /// <summary>Lifetime scale authored by the applying spell's charge, never written to its resource.</summary>
    public float DurationMultiplier { get; private set; }

    /// <summary>Current stack count (Fire ignite, Phase 29.5B). 1 for non-stacking effects; the per-tick
    /// DoT is multiplied by this.</summary>
    public int Stacks { get; private set; } = 1;

    /// <summary>Damage a ward can still absorb; 0 for a status that is not a ward.</summary>
    public float AbsorbRemaining { get; set; }

    /// <summary>What <see cref="AbsorbRemaining"/> started at (the share left decides the mana returned).</summary>
    public float AbsorbCapacity { get; set; }

    /// <summary>How many times this status has already jumped on a bearer's death (Stinging Swarm).</summary>
    public int SpreadGeneration { get; set; }

    /// <summary>Refreshes lifetime without shortening a stronger live application or postponing the
    /// next tick. Rapid stacking must not make an active damage-over-time effect stop dealing damage.</summary>
    public void Refresh(float durationMultiplier = 1f)
    {
        DurationMultiplier = StatusMath.DurationMultiplier(durationMultiplier);
        Remaining = System.Math.Max(Remaining, Definition.Duration * DurationMultiplier);
    }

    /// <summary>Re-application: refreshes the duration and adds one stack up to the definition's cap.</summary>
    public void AddStack(float durationMultiplier = 1f)
    {
        Refresh(durationMultiplier);
        Stacks = StatusMath.NextStack(Stacks, Definition.MaxStacks);
    }
}
