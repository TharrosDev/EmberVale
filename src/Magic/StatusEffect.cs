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
    public StatusEffect(StatusEffectResource definition, IEntity? source)
    {
        Definition = definition;
        Source = source;
        Remaining = definition.Duration;
        TickTimer = definition.TickInterval;
    }

    public StatusEffectResource Definition { get; }

    /// <summary>The caster that applied this effect; DoT kills are credited to it.</summary>
    public IEntity? Source { get; }

    public double Remaining { get; set; }

    public double TickTimer { get; set; }

    /// <summary>Current stack count (Fire ignite, Phase 29.5B). 1 for non-stacking effects; the per-tick
    /// DoT is multiplied by this.</summary>
    public int Stacks { get; private set; } = 1;

    /// <summary>Resets the lifetime (and tick) when the same effect is re-applied.</summary>
    public void Refresh()
    {
        Remaining = Definition.Duration;
        TickTimer = Definition.TickInterval;
    }

    /// <summary>Re-application: refreshes the duration and adds one stack up to the definition's cap.</summary>
    public void AddStack()
    {
        Refresh();
        Stacks = StatusMath.NextStack(Stacks, Definition.MaxStacks);
    }
}
