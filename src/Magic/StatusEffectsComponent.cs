using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Holds the timed status effects currently afflicting (or buffing) an entity and
/// ticks them each frame: damage-over-time is applied through the
/// <see cref="StatsComponent"/> (credited to the effect's source so DoT kills still
/// attribute), and stat modifiers (slows, wards) are pushed/pulled as
/// <see cref="StatModifier"/>s sourced to the effect instance. Re-applying an effect
/// refreshes its duration rather than stacking.
///
/// Effects are transient combat state — like poise/stagger they are intentionally
/// <em>not</em> persisted; a freshly loaded entity simply starts clean.
/// </summary>
[GlobalClass]
public partial class StatusEffectsComponent : EntityComponent
{
    private readonly Dictionary<string, StatusEffect> _active = new();
    private readonly List<string> _expired = new();

    private StatsComponent? _stats;

    /// <summary>The effects currently active on this entity (read-only, for UI).</summary>
    public IReadOnlyCollection<StatusEffect> ActiveEffects => _active.Values;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
    }

    protected override void OnTeardown()
    {
        ClearAll();
    }

    /// <summary>Applies (or refreshes) a status effect from its definition.</summary>
    public void Apply(StatusEffectResource? definition, IEntity? source)
    {
        if (definition == null || Entity == null || _stats is { IsAlive: false })
        {
            return;
        }

        if (_active.TryGetValue(definition.Id, out StatusEffect? existing))
        {
            existing.AddStack();
            return;
        }

        var effect = new StatusEffect(definition, source);
        _active[definition.Id] = effect;
        ApplyModifier(effect);
        EventBus.Instance?.Publish(new StatusEffectAppliedEvent(Entity, definition.Id, source));
    }

    public bool Has(string effectId) => _active.ContainsKey(effectId);

    /// <summary>Strips an effect outright (Phase 29.5D combos "consume" the status they trigger off, e.g.
    /// a shatter eating the chill). A no-op if the effect isn't present.</summary>
    public void Consume(string effectId) => Remove(effectId);

    public override void _Process(double delta)
    {
        if (_active.Count == 0)
        {
            return;
        }

        // A corpse keeps no afflictions: clear everything so modifiers don't linger.
        if (_stats is { IsAlive: false })
        {
            ClearAll();
            return;
        }

        foreach (StatusEffect effect in _active.Values)
        {
            Tick(effect, delta);
            if (effect.Remaining <= 0d)
            {
                _expired.Add(effect.Definition.Id);
            }
        }

        if (_expired.Count > 0)
        {
            foreach (string id in _expired)
            {
                Remove(id);
            }

            _expired.Clear();
        }
    }

    private void Tick(StatusEffect effect, double delta)
    {
        effect.Remaining -= delta;

        StatusEffectResource def = effect.Definition;
        if (!def.HasTickEffect)
        {
            return;
        }

        (int ticks, double newTimer) = StatusMath.AdvanceDot(effect.TickTimer, delta, def.TickInterval);
        effect.TickTimer = newTimer;
        for (int i = 0; i < ticks; i++)
        {
            if (def.HasDamageOverTime)
            {
                _stats?.ApplyDamage(def.DamagePerTick * effect.Stacks, effect.Source);
            }

            if (def.HasHealOverTime)
            {
                _stats?.Heal(def.HealPerTick);
            }

            // Stop ticking a victim the DoT just killed.
            if (_stats is { IsAlive: false })
            {
                break;
            }
        }
    }

    private void Remove(string effectId)
    {
        if (!_active.TryGetValue(effectId, out StatusEffect? effect))
        {
            return;
        }

        RemoveModifier(effect);
        _active.Remove(effectId);
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new StatusEffectRemovedEvent(Entity, effectId));
        }
    }

    private void ClearAll()
    {
        if (_active.Count == 0)
        {
            return;
        }

        foreach (StatusEffect effect in _active.Values)
        {
            RemoveModifier(effect);
            if (Entity != null)
            {
                EventBus.Instance?.Publish(new StatusEffectRemovedEvent(Entity, effect.Definition.Id));
            }
        }

        _active.Clear();
    }

    private void ApplyModifier(StatusEffect effect)
    {
        StatusEffectResource def = effect.Definition;
        if (_stats == null || !def.HasStatModifier)
        {
            return;
        }

        _stats.GetStat(def.ModStat).AddModifier(new StatModifier(def.ModValue, def.ModType, effect));
    }

    private void RemoveModifier(StatusEffect effect)
    {
        StatusEffectResource def = effect.Definition;
        if (_stats == null || !def.HasStatModifier)
        {
            return;
        }

        _stats.GetStat(def.ModStat).RemoveModifiersFromSource(effect);
    }
}
