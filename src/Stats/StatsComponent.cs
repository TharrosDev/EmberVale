using System;
using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Stats;

/// <summary>
/// The stats backbone for an entity. Owns one <see cref="Stat"/> per
/// <see cref="StatType"/> and, for resource stats, a current value that depletes
/// (health/stamina/mana). This is the foundation combat, progression, equipment
/// and UI all build on:
///   * Combat applies damage/heal and reads PhysicalPower/Armor/CritChance.
///   * Equipment/buffs push and pull <see cref="StatModifier"/>s.
///   * Progression raises base attribute values.
///
/// Persists its current resource values through <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class StatsComponent : EntityComponent, ISaveable
{
    private readonly Dictionary<StatType, Stat> _stats = new();
    private readonly Dictionary<StatType, float> _current = new();

    /// <summary>Optional authoring data seeding base values. Falls back to defaults.</summary>
    [Export]
    public AttributeSet? Attributes { get; set; }

    [ExportGroup("Passive Regen (per second)")]
    [Export] public float HealthRegen { get; set; } = 0f;
    [Export] public float StaminaRegen { get; set; } = 15f;
    [Export] public float ManaRegen { get; set; } = 4f;

    /// <summary>Seconds stamina regen is paused after any spend (Phase 29I anti-mash lever, DESIGN §1.4):
    /// mashing keeps resetting this, so a flurry drains to empty while spaced reads sustain.</summary>
    [Export] public float StaminaRegenDelay { get; set; } = 0.9f;

    private double _staminaIdle;

    public string SaveId => SaveKey("stats");

    public bool IsAlive => GetCurrent(StatType.Health) > 0f;

    protected override void OnInitialize()
    {
        BuildStats(Attributes ?? AttributeSet.CreateDefault());
        RefillResources();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public override void _Process(double delta)
    {
        Regenerate(StatType.Health, HealthRegen, delta);

        // Stamina regen is paused for StaminaRegenDelay after each spend (Phase 29I anti-mash).
        _staminaIdle += delta;
        if (StaminaPacing.CanRegen(_staminaIdle, StaminaRegenDelay))
        {
            Regenerate(StatType.Stamina, StaminaRegen, delta);
        }

        Regenerate(StatType.Mana, ManaRegen, delta);
    }

    private void Regenerate(StatType type, float rate, double delta)
    {
        if (rate <= 0f)
        {
            return;
        }

        // Never regenerate a corpse back to life; other resources may refill from 0.
        if (type == StatType.Health && !IsAlive)
        {
            return;
        }

        float current = GetCurrent(type);
        if (current >= GetMax(type))
        {
            return;
        }

        SetCurrent(type, current + (rate * (float)delta));
    }

    private void BuildStats(AttributeSet source)
    {
        IReadOnlyDictionary<StatType, float> baseValues = source.ToBaseValues();
        foreach (StatType type in Enum.GetValues<StatType>())
        {
            float baseValue = baseValues.TryGetValue(type, out float v) ? v : 0f;
            var stat = new Stat(type, baseValue);
            _stats[type] = stat;

            if (StatTypes.IsResource(type))
            {
                _current[type] = baseValue;
                // Keep current clamped to max whenever the max shifts (gear/buffs).
                stat.Changed += OnResourceMaxChanged;
            }
        }
    }

    /// <summary>Returns the <see cref="Stat"/> for a type, creating one if absent.</summary>
    public Stat GetStat(StatType type)
    {
        if (!_stats.TryGetValue(type, out Stat? stat))
        {
            stat = new Stat(type, 0f);
            _stats[type] = stat;
        }

        return stat;
    }

    /// <summary>The final, modifier-inclusive value of a stat (also the max for resources).</summary>
    public float GetValue(StatType type)
    {
        return GetStat(type).Value;
    }

    /// <summary>Current value of a resource stat; for non-resources returns the computed value.</summary>
    public float GetCurrent(StatType type)
    {
        if (StatTypes.IsResource(type))
        {
            return _current.TryGetValue(type, out float c) ? c : 0f;
        }

        return GetValue(type);
    }

    /// <summary>Max of a resource stat (its computed value).</summary>
    public float GetMax(StatType type)
    {
        return GetValue(type);
    }

    public float GetNormalized(StatType type)
    {
        float max = GetMax(type);
        return max <= 0f ? 0f : Mathf.Clamp(GetCurrent(type) / max, 0f, 1f);
    }

    /// <summary>Sets a resource's current value, clamped to [0, max], emitting change events.</summary>
    public void SetCurrent(StatType type, float value)
    {
        if (!StatTypes.IsResource(type))
        {
            return;
        }

        float max = GetMax(type);
        float clamped = Mathf.Clamp(value, 0f, max);
        float previous = _current.TryGetValue(type, out float c) ? c : 0f;
        if (Mathf.IsEqualApprox(previous, clamped))
        {
            return;
        }

        _current[type] = clamped;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ResourceChangedEvent(Entity, type, clamped, max));
        }
    }

    public void ModifyCurrent(StatType type, float delta)
    {
        // Any stamina spend resets the regen-delay clock (Phase 29I): the load that pauses regen.
        if (type == StatType.Stamina && delta < 0f)
        {
            _staminaIdle = 0d;
        }

        SetCurrent(type, GetCurrent(type) + delta);
    }

    /// <summary>Restores every resource stat to its maximum.</summary>
    public void RefillResources()
    {
        foreach (StatType type in _stats.Keys)
        {
            if (StatTypes.IsResource(type))
            {
                SetCurrent(type, GetMax(type));
            }
        }
    }

    // --- Combat-facing convenience helpers ---------------------------------

    /// <summary>Applies raw damage to health and raises damage/death events.
    /// <paramref name="source"/> is the attacker, threaded into the death event so
    /// progression can attribute the kill.</summary>
    public void ApplyDamage(float amount, IEntity? source = null)
    {
        if (Entity == null || amount <= 0f)
        {
            return;
        }

        float before = GetCurrent(StatType.Health);
        if (before <= 0f)
        {
            return;
        }

        SetCurrent(StatType.Health, before - amount);
        float after = GetCurrent(StatType.Health);

        EventBus.Instance?.Publish(new EntityDamagedEvent(Entity, amount, after));

        if (after <= 0f)
        {
            EventBus.Instance?.Publish(new EntityDiedEvent(Entity, source));
        }
    }

    public void Heal(float amount)
    {
        if (Entity == null || amount <= 0f || !IsAlive)
        {
            return;
        }

        ModifyCurrent(StatType.Health, amount);
        EventBus.Instance?.Publish(new EntityHealedEvent(Entity, amount, GetCurrent(StatType.Health)));
    }

    private void OnResourceMaxChanged(Stat stat)
    {
        // Re-clamp current to the (possibly new) max without inflating it.
        if (_current.TryGetValue(stat.Type, out float current))
        {
            SetCurrent(stat.Type, current);
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var resources = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<StatType, float> pair in _current)
        {
            resources[(int)pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary
        {
            ["resources"] = resources,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("resources", out Variant resourcesVariant))
        {
            return;
        }

        var resources = resourcesVariant.AsGodotDictionary();
        foreach (Variant key in resources.Keys)
        {
            var type = (StatType)key.AsInt32();
            SetCurrent(type, resources[key].AsSingle());
        }
    }
}
