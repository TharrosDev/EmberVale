using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.World;

/// <summary>
/// Runs the world's named events: on a cadence it rolls an eligible
/// <see cref="WorldEventResource"/> (by day phase, weight and per-event cooldown) and
/// starts it near the player — a raider band, a loot cache, or a champion hunt. It then
/// tracks the objective off gameplay events (<see cref="EntityDiedEvent"/> kills,
/// <see cref="ItemPickedUpEvent"/> collects), enforces a time limit, and on resolution
/// grants the authored rewards (XP, gold, an item, and reputation) through the player's
/// existing components. One event runs at a time so the world reads as a sequence of
/// discrete happenings rather than noise.
///
/// Reuses <see cref="EnemyFactory"/> and <see cref="ItemPickupFactory"/>. Like the
/// ambient <see cref="EncounterDirector"/> it is emergent/transient and not persisted —
/// only the rewards it grants (which flow through saved components) survive a reload.
/// </summary>
[GlobalClass]
public partial class WorldEventDirector : Node3D
{
    /// <summary>Average real seconds between world-event rolls (events are occasional).</summary>
    [Export] public float BaseIntervalSeconds { get; set; } = 75f;

    private readonly Dictionary<string, double> _cooldowns = new();

    private double _timer;
    private WorldEvent? _active;

    public WorldEvent? Active => _active;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _timer = NextInterval();

        ServiceLocator.Instance?.Register(this);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
    }

    public override void _ExitTree()
    {
        ServiceLocator.Instance?.Unregister<WorldEventDirector>();
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
    }

    /// <summary>Forces a specific event to start now (dev console). False if one is already
    /// active, the id is unknown, or there is no player to centre it on.</summary>
    public bool ForceStart(string eventId)
    {
        if (_active != null || WorldEventDatabase.Get(eventId) is not { } resource)
        {
            return false;
        }

        if (ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return false;
        }

        Begin(resource, player);
        return true;
    }

    public override void _Process(double delta)
    {
        if (GameManager.Instance is { IsPlaying: false })
        {
            return;
        }

        TickCooldowns(delta);

        if (_active != null)
        {
            TickActive(delta);
            return;
        }

        _timer -= delta;
        if (_timer <= 0d)
        {
            _timer = NextInterval();
            TryStart();
        }
    }

    private void TickActive(double delta)
    {
        WorldEvent active = _active!;
        if (active.IsTimed)
        {
            active.TimeLeft -= delta;
            if (active.TimeLeft <= 0d)
            {
                Fail(active);
            }
        }
    }

    // --- Starting -----------------------------------------------------------

    private void TryStart()
    {
        if (WorldEventDatabase.All.Count == 0 ||
            ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return;
        }

        WorldEventResource? resource = PickEligible(CurrentPhase());
        if (resource != null)
        {
            Begin(resource, player);
        }
    }

    private void Begin(WorldEventResource resource, PlayerCharacter player)
    {
        Vector3 origin = RingPointAround(player.GlobalPosition, resource);
        double limit = resource.TimeLimitSeconds > 0f ? resource.TimeLimitSeconds : double.PositiveInfinity;

        bool isCache = resource.Kind == WorldEventKind.Cache;
        int required = isCache ? Mathf.Max(1, resource.CacheQuantity) : Mathf.Max(1, resource.RollCount());

        var worldEvent = new WorldEvent(resource, origin, required, limit);
        if (isCache)
        {
            SpawnCache(worldEvent);
        }
        else
        {
            SpawnCombat(worldEvent, required);
        }

        _active = worldEvent;
        EventBus.Instance?.Publish(new WorldEventStartedEvent(resource.Id, resource.DisplayName, origin));
        Log.Info($"World event: {resource.DisplayName} — {worldEvent.ObjectiveLabel()}.");
    }

    private void SpawnCombat(WorldEvent worldEvent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 jitter = new(GD.Randf() * 2f - 1f, 0f, GD.Randf() * 2f - 1f);
            EnemyEntity enemy = EnemyFactory.Create(worldEvent.Origin + jitter);
            GetParent().AddChild(enemy);

            ApplyHealthMultiplier(enemy, worldEvent.Resource.HealthMultiplier);
            worldEvent.Enemies.Add(enemy);
            worldEvent.EnemyIds.Add(enemy.RuntimeId);
        }
    }

    private void SpawnCache(WorldEvent worldEvent)
    {
        WorldEventResource r = worldEvent.Resource;
        if (ItemDatabase.Get(r.CacheItemId) is { } item)
        {
            GetParent().AddChild(ItemPickupFactory.Create(item, Mathf.Max(1, r.CacheQuantity), worldEvent.Origin));
        }
    }

    private static void ApplyHealthMultiplier(EnemyEntity enemy, float multiplier)
    {
        if (multiplier <= 1f || enemy.GetComponent<StatsComponent>() is not { } stats)
        {
            return;
        }

        stats.GetStat(StatType.Health).AddModifier(
            new StatModifier(multiplier - 1f, ModifierType.PercentMult, "world_event.champion"));
        stats.RefillResources();
    }

    // --- Objective tracking -------------------------------------------------

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (_active is not { Status: WorldEventStatus.Active } active ||
            active.Resource.Kind == WorldEventKind.Cache)
        {
            return;
        }

        if (!active.EnemyIds.Remove(e.Entity.RuntimeId))
        {
            return;
        }

        Advance(active, 1);
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        if (_active is not { Status: WorldEventStatus.Active } active ||
            active.Resource.Kind != WorldEventKind.Cache)
        {
            return;
        }

        if (e.Item.Id != active.Resource.CacheItemId)
        {
            return;
        }

        Advance(active, e.Quantity);
    }

    private void Advance(WorldEvent active, int amount)
    {
        active.Progress = Mathf.Min(active.Progress + amount, active.Required);
        EventBus.Instance?.Publish(new WorldEventProgressEvent(active.Resource.Id, active.Progress, active.Required));

        if (active.IsComplete)
        {
            Complete(active);
        }
    }

    // --- Resolution ---------------------------------------------------------

    private void Complete(WorldEvent active)
    {
        active.Status = WorldEventStatus.Completed;
        GrantRewards(active.Resource);
        Log.Info($"World event complete: {active.Resource.DisplayName}.");
        End(active, completed: true);
    }

    private void Fail(WorldEvent active)
    {
        active.Status = WorldEventStatus.Failed;

        // Tidy up any raiders the player never dealt with so they don't linger forever.
        foreach (EnemyEntity enemy in active.Enemies)
        {
            if (IsInstanceValid(enemy))
            {
                enemy.QueueFree();
            }
        }

        Log.Info($"World event failed: {active.Resource.DisplayName}.");
        End(active, completed: false);
    }

    private void End(WorldEvent active, bool completed)
    {
        _cooldowns[active.Resource.Id] = active.Resource.CooldownSeconds;
        _active = null;
        _timer = NextInterval();
        EventBus.Instance?.Publish(new WorldEventEndedEvent(active.Resource.Id, active.Resource.DisplayName, completed));
    }

    private void GrantRewards(WorldEventResource r)
    {
        if (ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return;
        }

        if (r.XpReward > 0)
        {
            player.GetComponent<ProgressionComponent>()?.AddXp(r.XpReward);
        }

        InventoryComponent? inventory = player.GetComponent<InventoryComponent>();
        if (inventory != null)
        {
            if (r.GoldReward > 0 && ItemDatabase.Get("item.currency.gold") is { } gold)
            {
                inventory.AddItem(gold, r.GoldReward);
            }

            if (!string.IsNullOrEmpty(r.RewardItemId) &&
                r.RewardItemQuantity > 0 &&
                ItemDatabase.Get(r.RewardItemId) is { } item)
            {
                inventory.AddItem(item, r.RewardItemQuantity);
            }
        }

        if (!string.IsNullOrEmpty(r.FactionRewardId) && r.FactionRewardAmount != 0)
        {
            player.GetComponent<ReputationComponent>()?.Add(r.FactionRewardId, r.FactionRewardAmount);
        }
    }

    // --- Selection helpers --------------------------------------------------

    private WorldEventResource? PickEligible(DayPhase phase)
    {
        var pool = new List<WorldEventResource>();
        float total = 0f;
        foreach (WorldEventResource r in WorldEventDatabase.All)
        {
            if (!r.AllowedIn(phase) || OnCooldown(r.Id))
            {
                continue;
            }

            pool.Add(r);
            total += Mathf.Max(0f, r.SelectionWeight);
        }

        if (pool.Count == 0 || total <= 0f)
        {
            return null;
        }

        float roll = GD.Randf() * total;
        foreach (WorldEventResource r in pool)
        {
            roll -= Mathf.Max(0f, r.SelectionWeight);
            if (roll <= 0f)
            {
                return r;
            }
        }

        return pool[pool.Count - 1];
    }

    private bool OnCooldown(string id) => _cooldowns.TryGetValue(id, out double remaining) && remaining > 0d;

    private void TickCooldowns(double delta)
    {
        if (_cooldowns.Count == 0)
        {
            return;
        }

        foreach (string id in new List<string>(_cooldowns.Keys))
        {
            double remaining = _cooldowns[id] - delta;
            if (remaining <= 0d)
            {
                _cooldowns.Remove(id);
            }
            else
            {
                _cooldowns[id] = remaining;
            }
        }
    }

    private Vector3 RingPointAround(Vector3 center, WorldEventResource r)
    {
        float angle = GD.Randf() * Mathf.Tau;
        float distance = Mathf.Lerp(r.SpawnDistanceMin, r.SpawnDistanceMax, GD.Randf());
        return new Vector3(
            center.X + (Mathf.Cos(angle) * distance),
            0.5f,
            center.Z + (Mathf.Sin(angle) * distance));
    }

    private double NextInterval()
    {
        float jitter = 0.7f + (GD.Randf() * 0.6f); // 0.7..1.3
        return Mathf.Max(5f, BaseIntervalSeconds * jitter);
    }

    private static DayPhase CurrentPhase()
    {
        return ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out WorldClock clock)
            ? clock.Phase
            : DayPhase.Day;
    }
}
