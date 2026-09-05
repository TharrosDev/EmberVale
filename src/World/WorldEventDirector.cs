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
using Embervale.Save;
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
/// Reuses <see cref="EnemyFactory"/> and <see cref="ItemPickupFactory"/>. Its compact objective and
/// cooldown state is session/save owned; its actors are materialized under the active streaming
/// cell only while their origin is Near.
/// </summary>
[GlobalClass]
public partial class WorldEventDirector : Node3D, ISaveable
{
    /// <summary>Average real seconds between world-event rolls (events are occasional).</summary>
    [Export] public float BaseIntervalSeconds { get; set; } = 75f;

    private readonly Dictionary<string, double> _cooldowns = new();

    /// <summary>Scratch list of cooldowns that expired this tick, reused every frame (see
    /// <see cref="TickCooldowns"/>). Mirrors <c>SpellcastingComponent._expiring</c>.</summary>
    private readonly List<string> _expiring = new();

    private double _timer;
    private WorldEvent? _active;

    public string SaveId => "world_events";

    public WorldEvent? Active => _active;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _timer = NextInterval();

        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Subscribe<RegionChangedEvent>(OnRegionTransition);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Unsubscribe<RegionChangedEvent>(OnRegionTransition);
    }

    /// <summary>A region transition leaves an in-progress event's raiders behind in the old region.
    /// Abort it through the existing <see cref="Fail"/> path so they're despawned and the cooldown is
    /// stamped — no orphaned spawns or stuck <c>_active</c> carried across the boundary.</summary>
    private void OnRegionTransition(RegionChangedEvent e)
    {
        if (_active != null)
        {
            Fail(_active);
        }
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
        RemoveInvalidActors(active);
        if (active.Actors.Count == 0 && !active.IsComplete && OriginIsNear(active.Origin))
        {
            active.Enemies.Clear();
            active.EnemyIds.Clear();
            Materialize(active);
        }
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

        // The active region gates the pool (Phase 35G, mirroring 34.5B's encounter gate) — the
        // streamer is re-configured on every region change, so it is the one thing that always
        // knows where the player is standing.
        string regionId = ServiceLocator.Instance.TryGet(out RegionStreamer streamer)
            ? streamer.ActiveRegionId
            : string.Empty;

        WorldEventResource? resource = PickEligible(CurrentPhase(), regionId);
        if (resource != null)
        {
            Begin(resource, player);
        }
    }

    private void Begin(WorldEventResource resource, PlayerCharacter player)
    {
        bool isCache = resource.Kind == WorldEventKind.Cache;

        // A loot cache is harmless and may appear anywhere; a hostile event must land outside the
        // town's safe zone. If it can't (player deep in town), skip and re-roll next interval.
        Vector3 origin;
        if (isCache)
        {
            origin = RingPointAround(player.GlobalPosition, resource);
        }
        else if (!SafeZones.TryRingPointOutside(player.GlobalPosition, resource.SpawnDistanceMin, resource.SpawnDistanceMax, out origin))
        {
            return;
        }

        double limit = resource.TimeLimitSeconds > 0f ? resource.TimeLimitSeconds : double.PositiveInfinity;
        int required = isCache ? Mathf.Max(1, resource.CacheQuantity) : Mathf.Max(1, resource.RollCount());

        var worldEvent = new WorldEvent(resource, origin, required, limit);
        if (!OriginIsNear(origin) || !Materialize(worldEvent))
        {
            return;
        }

        _active = worldEvent;
        EventBus.Instance?.Publish(new WorldEventStartedEvent(resource.Id, resource.NameKey, origin));
        Log.Info($"World event: {worldEvent.Name} — {worldEvent.ObjectiveLabel()}.");
    }

    private bool Materialize(WorldEvent worldEvent)
    {
        int remaining = Mathf.Max(0, worldEvent.Required - worldEvent.Progress);
        bool complete = worldEvent.Resource.Kind == WorldEventKind.Cache
            ? SpawnCache(worldEvent, remaining)
            : SpawnCombat(worldEvent, remaining);
        if (!complete)
        {
            ClearMaterialized(worldEvent);
        }
        return complete;
    }

    private bool SpawnCombat(WorldEvent worldEvent, int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            // ⚠️ Each member is placed on the ground it personally lands on, not on the group
            // origin's. The jitter is up to a metre in each direction and the origin was validated
            // once for the whole band, so on any slope some of them spawned inside it.
            Vector3 jitter = new(GD.Randf() * 2f - 1f, 0f, GD.Randf() * 2f - 1f);
            EnemyEntity enemy = EnemyTemplateRegistry.Create(
                worldEvent.Resource.EnemyTemplateId,
                SpawnPlacement.Resolve(this, worldEvent.Origin + jitter));
            if (!TryOwnActor(enemy, enemy.Position))
            {
                enemy.Free();
                continue;
            }

            EnemyScaling.ApplyHealthMultiplier(enemy, worldEvent.Resource.HealthMultiplier, "world_event.champion");
            worldEvent.Enemies.Add(enemy);
            worldEvent.Actors.Add(enemy);
            worldEvent.EnemyIds.Add(enemy.RuntimeId);
            spawned++;
        }
        return spawned == count;
    }

    private bool SpawnCache(WorldEvent worldEvent, int count)
    {
        WorldEventResource r = worldEvent.Resource;
        if (count > 0 && ItemDatabase.Get(r.CacheItemId) is { } item)
        {
            // On the ground, not at the event's authored Y: an event origin is a planar point and
            // WorldEvents places it from a safe-zone ring at a fixed height (see SafeZones), which on
            // real terrain is inside a hillside as often as above it.
            Vector3 pickupPosition = SafePlacementService.TryResolve(
                this, worldEvent.Origin, out Vector3 resolved,
                capsuleRadius: 0.25f, capsuleHeight: 0.5f)
                ? resolved
                : WorldGround.OnGround(worldEvent.Origin, 0.31f);
            var pickup = ItemPickupFactory.Create(item, count, pickupPosition);
            if (TryOwnActor(pickup, pickup.Position))
            {
                worldEvent.Actors.Add(pickup);
                return true;
            }
            pickup.Free();
        }
        return false;
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
        Log.Info($"World event complete: {active.Name}.");
        End(active, completed: true);
    }

    private void Fail(WorldEvent active)
    {
        active.Status = WorldEventStatus.Failed;

        // Tidy up any raiders the player never dealt with so they don't linger forever.
        foreach (Node3D actor in active.Actors)
        {
            if (IsInstanceValid(actor))
            {
                actor.QueueFree();
            }
        }

        Log.Info($"World event failed: {active.Name}.");
        End(active, completed: false);
    }

    private void End(WorldEvent active, bool completed)
    {
        _cooldowns[active.Resource.Id] = active.Resource.CooldownSeconds;
        _active = null;
        _timer = NextInterval();
        EventBus.Instance?.Publish(new WorldEventEndedEvent(active.Resource.Id, active.Resource.NameKey, completed));
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

        // Through ItemGrant so a full pack spills the payout at the player's feet instead of
        // destroying it — the same reason quest rewards go that way. See Items/ItemGrant.cs.
        InventoryComponent? inventory = player.GetComponent<InventoryComponent>();
        if (r.GoldReward > 0 && ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
        {
            ItemGrant.Give(inventory, gold, r.GoldReward, player);
        }

        if (!string.IsNullOrEmpty(r.RewardItemId) &&
            r.RewardItemQuantity > 0 &&
            ItemDatabase.Get(r.RewardItemId) is { } item)
        {
            ItemGrant.Give(inventory, item, r.RewardItemQuantity, player);
        }

        if (!string.IsNullOrEmpty(r.FactionRewardId) && r.FactionRewardAmount != 0)
        {
            player.GetComponent<ReputationComponent>()?.Add(r.FactionRewardId, r.FactionRewardAmount);
        }
    }

    // --- Selection helpers --------------------------------------------------

    private WorldEventResource? PickEligible(DayPhase phase, string regionId)
    {
        var pool = new List<WorldEventResource>();
        float total = 0f;
        foreach (WorldEventResource r in WorldEventDatabase.All)
        {
            if (!r.AllowedIn(phase) || !r.AllowedIn(regionId) || OnCooldown(r.Id))
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

        // Reusable buffer, not a fresh List every frame — the same fix SpellcastingComponent.TickCooldowns
        // already carries, which this had not picked up. Authored world-event cooldowns are stamped in
        // real seconds, so this dictionary stays non-empty for minutes at a stretch and the old snapshot
        // was ~60 allocations a second, indefinitely, for the whole session.
        // Updating an existing value in place mid-enumeration is fine on .NET 8; removing is not.
        _expiring.Clear();
        foreach (KeyValuePair<string, double> entry in _cooldowns)
        {
            double remaining = entry.Value - delta;
            if (remaining <= 0d)
            {
                _expiring.Add(entry.Key);
            }
            else
            {
                _cooldowns[entry.Key] = remaining;
            }
        }

        foreach (string id in _expiring)
        {
            _cooldowns.Remove(id);
        }
    }

    /// <summary>The cache path's ring point. ⚠️ The Y comes from the ground — the literal 0.5 that
    /// was here is the same defect <see cref="SafeZones.TryRingPointOutside"/> already carries a
    /// warning about: on real terrain a fixed spawn height is inside a hillside as often as above
    /// one, and a loot cache buried in a slope is one the player can see and never reach.</summary>
    private Vector3 RingPointAround(Vector3 center, WorldEventResource r)
    {
        float angle = GD.Randf() * Mathf.Tau;
        float distance = Mathf.Lerp(r.SpawnDistanceMin, r.SpawnDistanceMax, GD.Randf());
        float x = center.X + (Mathf.Cos(angle) * distance);
        float z = center.Z + (Mathf.Sin(angle) * distance);
        return new Vector3(x, WorldGround.HeightAt(x, z) + 0.5f, z);
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

    private static bool OriginIsNear(Vector3 origin) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer) &&
        streamer.IsPositionReady(origin);

    private static bool TryOwnActor(Node3D actor, Vector3 position) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer) &&
        streamer.TryAddCellOwnedActor(actor, position);

    private static void RemoveInvalidActors(WorldEvent worldEvent)
    {
        worldEvent.Actors.RemoveAll(actor => !IsInstanceValid(actor) || !actor.IsInsideTree());
        worldEvent.Enemies.RemoveAll(enemy => !IsInstanceValid(enemy) || !enemy.IsInsideTree());
    }

    private static void ClearMaterialized(WorldEvent worldEvent)
    {
        foreach (Node3D actor in worldEvent.Actors)
        {
            if (IsInstanceValid(actor))
            {
                actor.QueueFree();
            }
        }
        worldEvent.Actors.Clear();
        worldEvent.Enemies.Clear();
        worldEvent.EnemyIds.Clear();
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var cooldowns = new Godot.Collections.Array();
        foreach (KeyValuePair<string, double> cooldown in _cooldowns)
        {
            cooldowns.Add(new Godot.Collections.Dictionary
            {
                ["id"] = cooldown.Key,
                ["remaining"] = cooldown.Value,
            });
        }

        var data = new Godot.Collections.Dictionary
        {
            ["timer"] = _timer,
            ["cooldowns"] = cooldowns,
        };
        if (_active is { } active)
        {
            data["active"] = new Godot.Collections.Dictionary
            {
                ["id"] = active.Resource.Id,
                ["x"] = active.Origin.X,
                ["y"] = active.Origin.Y,
                ["z"] = active.Origin.Z,
                ["required"] = active.Required,
                ["progress"] = active.Progress,
                ["time_left"] = active.IsTimed ? active.TimeLeft : -1d,
            };
        }
        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (_active != null)
        {
            foreach (Node3D actor in _active.Actors)
            {
                if (IsInstanceValid(actor))
                {
                    actor.QueueFree();
                }
            }
            _active = null;
        }

        _cooldowns.Clear();
        if (data.TryGetValue("cooldowns", out Variant cooldownVariant) &&
            cooldownVariant.VariantType == Variant.Type.Array)
        {
            foreach (Variant element in cooldownVariant.AsGodotArray())
            {
                if (element.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }
                Godot.Collections.Dictionary entry = element.AsGodotDictionary();
                string id = entry.TryGetValue("id", out Variant idValue) ? idValue.AsString() : string.Empty;
                double remaining = entry.TryGetValue("remaining", out Variant remainingValue)
                    ? remainingValue.AsDouble()
                    : 0d;
                if (!string.IsNullOrEmpty(id) && remaining > 0d)
                {
                    _cooldowns[id] = remaining;
                }
            }
        }
        _timer = data.TryGetValue("timer", out Variant timerValue)
            ? timerValue.AsDouble()
            : NextInterval();

        if (!data.TryGetValue("active", out Variant activeVariant) ||
            activeVariant.VariantType != Variant.Type.Dictionary)
        {
            return;
        }
        Godot.Collections.Dictionary saved = activeVariant.AsGodotDictionary();
        string eventId = saved.TryGetValue("id", out Variant eventIdValue)
            ? eventIdValue.AsString()
            : string.Empty;
        if (WorldEventDatabase.Get(eventId) is not { } resource)
        {
            return;
        }
        var origin = new Vector3(
            saved.TryGetValue("x", out Variant x) ? x.AsSingle() : 0f,
            saved.TryGetValue("y", out Variant y) ? y.AsSingle() : 0f,
            saved.TryGetValue("z", out Variant z) ? z.AsSingle() : 0f);
        int required = saved.TryGetValue("required", out Variant requiredValue)
            ? requiredValue.AsInt32()
            : 1;
        double savedTime = saved.TryGetValue("time_left", out Variant timeValue)
            ? timeValue.AsDouble()
            : -1d;
        var restored = new WorldEvent(
            resource, origin, required, savedTime < 0d ? double.PositiveInfinity : savedTime)
        {
            Progress = saved.TryGetValue("progress", out Variant progressValue)
                ? progressValue.AsInt32()
                : 0,
        };
        _active = restored;
        // TickActive materializes after the destination cell's collision is Near. Loading never
        // creates actors into a region that has not finished streaming.
        EventBus.Instance?.Publish(new WorldEventStartedEvent(resource.Id, resource.NameKey, origin));
    }
}
