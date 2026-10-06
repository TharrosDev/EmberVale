using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// The timed half of using a consumable: the cooldown groups, and restores that are spread over a
/// duration. Sits beside the <see cref="InventoryComponent"/> on an actor that drinks potions (the
/// player). <see cref="Check"/> and <see cref="Apply"/> are static because an actor without this
/// component can still use a consumable: it simply has no cooldowns and takes an over-time restore
/// all at once.
///
/// <para>A timed stat buff is not tracked here. It is applied as a beneficial status through the
/// <see cref="StatusEffectsComponent"/>, which already owns expiry, the modifier's removal, death,
/// load and the HUD chip. Nothing here is saved: a cooldown and a half-drunk draught are a few
/// seconds of state, and both are dropped on load so a quickload never inherits them.</para>
/// </summary>
[GlobalClass]
public partial class ConsumableEffectsComponent : EntityComponent
{
    private sealed class Restore
    {
        public StatType Stat;
        public float PerSecond;
        public float Remaining;
    }

    /// <summary>One status definition per buff consumable, built on first use and kept: the status
    /// system keys an active effect by its definition's id.</summary>
    private static readonly Dictionary<string, StatusEffectResource> BuffDefinitions = new();

    private readonly Dictionary<string, float> _cooldowns = new();
    private readonly Dictionary<string, float> _cooldownTotals = new();
    private readonly List<Restore> _restores = new();
    private readonly List<string> _expired = new();
    private StatsComponent? _stats;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnGameLoading);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        _cooldowns.Clear();
        _cooldownTotals.Clear();
        _restores.Clear();
    }

    private void OnGameLoading(GameLoadingEvent _)
    {
        _cooldowns.Clear();
        _cooldownTotals.Clear();
        _restores.Clear();
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        // A draught does not keep working on a corpse, or on whoever stands up at the respawn point.
        if (ReferenceEquals(e.Entity, Entity))
        {
            _restores.Clear();
        }
    }

    /// <summary>Seconds until a consumable sharing this key can be used again; 0 when ready.</summary>
    public float CooldownRemaining(string cooldownKey) =>
        _cooldowns.TryGetValue(cooldownKey, out float remaining) ? remaining : 0f;

    public float CooldownRemaining(ConsumableItemResource item) => CooldownRemaining(item.CooldownKey);

    /// <summary>1 just after a use, falling to 0 when ready: what a hotbar sweep draws.</summary>
    public float CooldownFraction(string cooldownKey) => ConsumableRules.CooldownFraction(
        CooldownRemaining(cooldownKey), _cooldownTotals.TryGetValue(cooldownKey, out float total) ? total : 0f);

    public float CooldownFraction(ConsumableItemResource item) => CooldownFraction(item.CooldownKey);

    public void StartCooldown(string cooldownKey, float seconds)
    {
        if (seconds <= 0f || string.IsNullOrEmpty(cooldownKey))
        {
            return;
        }

        _cooldowns[cooldownKey] = seconds;
        _cooldownTotals[cooldownKey] = seconds;
    }

    public override void _Process(double delta)
    {
        float step = (float)delta;
        if (_cooldowns.Count > 0)
        {
            _expired.Clear();
            foreach (string key in new List<string>(_cooldowns.Keys))
            {
                float left = ConsumableRules.TickCooldown(_cooldowns[key], step);
                if (left <= 0f)
                {
                    _expired.Add(key);
                }
                else
                {
                    _cooldowns[key] = left;
                }
            }

            foreach (string key in _expired)
            {
                _cooldowns.Remove(key);
                _cooldownTotals.Remove(key);
            }
        }

        if (_restores.Count == 0 || _stats == null)
        {
            return;
        }

        if (!_stats.IsAlive)
        {
            _restores.Clear();
            return;
        }

        for (int i = _restores.Count - 1; i >= 0; i--)
        {
            Restore restore = _restores[i];
            float amount = ConsumableRules.RestoreStep(restore.PerSecond, restore.Remaining, step, out float left);
            restore.Remaining = left;

            // ModifyCurrent rather than Heal: a heal event per frame would be a hundred toasts' worth
            // of feedback for one potion. The instant heal below keeps announcing itself.
            _stats.ModifyCurrent(restore.Stat, amount);
            if (left <= 0f)
            {
                _restores.RemoveAt(i);
            }
        }
    }

    // --- The rules against live state. Static: see the class remarks. ---

    /// <summary>Whether <paramref name="owner"/> may use <paramref name="item"/> right now.</summary>
    public static ConsumeRefusal Check(IEntity owner, ConsumableItemResource item)
    {
        StatsComponent? stats = owner.GetComponent<StatsComponent>();
        StatType resource = ResourceOf(item.Effect);
        float cooldown = owner.GetComponent<ConsumableEffectsComponent>()?.CooldownRemaining(item) ?? 0f;
        return ConsumableRules.Check(
            item.Effect,
            ConsumableRules.RestoreAmount(item.Effect, item.Magnitude, item.HealAmount),
            cooldown,
            stats?.GetCurrent(resource) ?? 0f,
            stats?.GetMax(resource) ?? 0f,
            item.Effect == ConsumableEffectKind.Cure ? Curable(owner, item).Count : 0);
    }

    /// <summary>Applies <paramref name="item"/>'s effect to <paramref name="owner"/> and starts its
    /// cooldown. The caller has already checked <see cref="Check"/> and removed the unit.</summary>
    public static void Apply(IEntity owner, ConsumableItemResource item)
    {
        StatsComponent? stats = owner.GetComponent<StatsComponent>();
        ConsumableEffectsComponent? timed = owner.GetComponent<ConsumableEffectsComponent>();
        StatusEffectsComponent? statuses = owner.GetComponent<StatusEffectsComponent>();

        switch (item.Effect)
        {
            case ConsumableEffectKind.Heal:
            case ConsumableEffectKind.RestoreStamina:
            case ConsumableEffectKind.RestoreMana:
            {
                float amount = ConsumableRules.RestoreAmount(item.Effect, item.Magnitude, item.HealAmount);
                if (amount <= 0f || stats == null)
                {
                    break;
                }

                StatType resource = ResourceOf(item.Effect);
                if (timed != null && ConsumableRules.IsOverTime(item.Effect, item.DurationSeconds))
                {
                    timed._restores.Add(new Restore
                    {
                        Stat = resource,
                        PerSecond = amount / item.DurationSeconds,
                        Remaining = item.DurationSeconds,
                    });
                }
                else if (resource == StatType.Health)
                {
                    stats.Heal(amount);
                }
                else
                {
                    stats.ModifyCurrent(resource, amount);
                }

                break;
            }

            case ConsumableEffectKind.Buff:
                if (statuses == null)
                {
                    Log.Warn($"{owner.DisplayName} drank '{item.Id}' but has no status effects component; the buff was lost.");
                }
                else
                {
                    statuses.Apply(BuffDefinition(item), owner);
                }

                break;

            case ConsumableEffectKind.Cure:
                foreach (string id in Curable(owner, item))
                {
                    statuses?.Cleanse(id, owner);
                }

                break;
        }

        timed?.StartCooldown(item.CooldownKey, item.CooldownSeconds);
    }

    /// <summary>The status id a buff consumable's effect runs under (<c>status.consumable.&lt;leaf&gt;</c>).</summary>
    public static string BuffStatusId(string itemId)
    {
        int dot = itemId.LastIndexOf('.');
        return "status.consumable." + (dot >= 0 ? itemId[(dot + 1)..] : itemId);
    }

    private static StatType ResourceOf(ConsumableEffectKind effect)
    {
        return effect switch
        {
            ConsumableEffectKind.RestoreStamina => StatType.Stamina,
            ConsumableEffectKind.RestoreMana => StatType.Mana,
            _ => StatType.Health,
        };
    }

    /// <summary>The active statuses a cure would remove: the listed ids it finds, or every harmful
    /// one when the list is empty. A status that cannot be dispelled is never curable.</summary>
    private static List<string> Curable(IEntity owner, ConsumableItemResource item)
    {
        var ids = new List<string>();
        if (owner.GetComponent<StatusEffectsComponent>() is not { } statuses)
        {
            return ids;
        }

        foreach (StatusEffect effect in statuses.ActiveEffects)
        {
            StatusEffectResource definition = effect.Definition;
            if (!definition.Dispellable)
            {
                continue;
            }

            bool named = item.CureStatusIds.Count > 0
                ? item.CureStatusIds.Contains(definition.Id)
                : !definition.IsBeneficial;
            if (named)
            {
                ids.Add(definition.Id);
            }
        }

        return ids;
    }

    private static StatusEffectResource BuffDefinition(ConsumableItemResource item)
    {
        if (BuffDefinitions.TryGetValue(item.Id, out StatusEffectResource? cached))
        {
            return cached;
        }

        var definition = new StatusEffectResource
        {
            Id = BuffStatusId(item.Id),
            DisplayName = item.DisplayName,
            IsBeneficial = true,
            Duration = Mathf.Max(0.1f, item.DurationSeconds),
            MaxStacks = 1,
            ModStat = item.BuffStat,
            ModType = item.BuffKind,
            ModValue = item.Magnitude,
        };
        BuffDefinitions[item.Id] = definition;
        return definition;
    }
}
