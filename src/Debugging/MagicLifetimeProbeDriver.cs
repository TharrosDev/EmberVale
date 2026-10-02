using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Bootstrap;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>Native delivery fixtures for tools/magic_lifetime_probe.gd. Uses a real, unbuilt
/// GameSession and authored spells; GDScript owns the frame boundaries and reports assertions.</summary>
public partial class MagicLifetimeProbeDriver : RefCounted
{
    private readonly List<Node3D> _deliveries = new();
    private GameSession _session = null!;
    private Entity _caster = null!;
    private StatsComponent _casterStats = null!;
    private StatsComponent _targetStats = null!;
    private SpellProjectile _shot = null!;
    private SpellGround _ground = null!;
    private SpellZone _zone = null!;
    private SpellBarrier _barrier = null!;
    private SpellTotem _totem = null!;
    private NodePool<SpellProjectile>? _pool;
    private float _casterHealth;
    private float _targetHealth;
    private int _loadingBaseline;
    private int _despawnBaseline;
    private ulong _pooledId;
    public int PoolReturns { get; private set; }
    public bool ReusedSameShot { get; private set; }

    public void Begin(Node parent)
    {
        _deliveries.Clear();
        _session = new GameSession();
        parent.AddChild(_session); // no Build: production ownership without world streaming
        _caster = Actor("LifetimeCaster", 0);
        _session.Players.AddChild(_caster);
        _caster.Position = new Vector3(0f, 0f, 4f);
        Entity target = Actor("LifetimeTarget", 1);
        _session.World.AddChild(target);
        _casterStats = _caster.GetComponent<StatsComponent>()!;
        _targetStats = target.GetComponent<StatsComponent>()!;
        _casterStats.SetCurrent(StatType.Health, _casterStats.GetMax(StatType.Health) * 0.5f);
        _loadingBaseline = EventBus.Instance.SubscriberCount<GameLoadingEvent>();
        _despawnBaseline = EventBus.Instance.SubscriberCount<EntityDespawnedEvent>();
        PoolReturns = 0;
        ReusedSameShot = true;
    }

    public bool ControlEffectsWork()
    {
        float beforeTarget = _targetStats.GetCurrent(StatType.Health);
        float beforeCaster = _casterStats.GetCurrent(StatType.Health);
        _zone._Process(0d);
        _totem._Process(1.01d);
        GD.Print($"lifetime controls: target {beforeTarget} -> {_targetStats.GetCurrent(StatType.Health)}, " +
            $"caster {beforeCaster} -> {_casterStats.GetCurrent(StatType.Health)}");
        bool worked = _targetStats.GetCurrent(StatType.Health) < beforeTarget &&
            _casterStats.GetCurrent(StatType.Health) > beforeCaster;
        foreach (Node3D delivery in _deliveries) delivery.Free();
        _deliveries.Clear();
        return worked;
    }

    public void SpawnAll()
    {
        SpellResource bolt = Spell("spell.ball_lightning");
        SpellResource ground = Spell("spell.sunfall");
        SpellResource zone = Spell("spell.blizzard");
        SpellResource barrier = Spell("spell.glacial_bulwark");
        SpellResource totem = Spell("spell.lifebloom_totem");
        _shot = Attach(new SpellProjectile(), new Vector3(20f, 2f, 20f));
        _shot.Launch(bolt, Packet(bolt), _caster, 0, Vector3.Right);
        Freeze(_shot);
        _ground = Attach(new SpellGround { Spell = ground, Packet = Packet(ground), Caster = _caster,
            CasterTeam = 0, Delay = ground.GroundDelay, Radius = ground.ImpactRadius }, Vector3.Zero);
        _zone = Attach(new SpellZone { Spell = zone, Packet = Packet(zone), Caster = _caster,
            CasterTeam = 0, Radius = zone.ImpactRadius, Duration = zone.ZoneDuration,
            TickInterval = zone.ZoneTickInterval }, Vector3.Zero);
        _barrier = Attach(new SpellBarrier { Spell = barrier, Packet = Packet(barrier), Caster = _caster,
            CasterTeam = 0, Delay = 0f, Width = barrier.BarrierWidth,
            Duration = barrier.BarrierDuration }, new Vector3(15f, 0f, 0f));
        _totem = Attach(new SpellTotem { Spell = totem, Caster = _caster, CasterTeam = 0,
            Target = _casterStats, HealPerTick = totem.Healing, Duration = totem.SummonDuration,
            TickInterval = totem.SummonTickInterval }, new Vector3(0f, 0f, 6f));
        _casterHealth = _casterStats.GetCurrent(StatType.Health);
        _targetHealth = _targetStats.GetCurrent(StatType.Health);
    }

    public bool OwnedBySession() => _deliveries.Count == 5 &&
        _deliveries.All(n => n.GetParent() == _session);

    public void PreLoad() => EventBus.Instance.Publish(new GameLoadingEvent("magic_lifetime_probe"));

    public void DespawnEvent() => EventBus.Instance.Publish(new EntityDespawnedEvent(_caster));

    public void DetachCaster()
    {
        _caster.GetParent().RemoveChild(_caster);
        _caster.QueueFree();
    }

    public void QueueCaster() => _caster.QueueFree();

    public string[] InertIssues()
    {
        var issues = new List<string>();
        // Deliberately invoke methods even though cancellation disabled automatic processing.
        _shot._PhysicsProcess(100d);
        _ground._Process(100d);
        _ground._PhysicsProcess(100d);
        _zone._Process(100d);
        _zone._PhysicsProcess(100d);
        _barrier._Process(100d);
        _totem._Process(100d);
        if (_targetStats.GetCurrent(StatType.Health) != _targetHealth) issues.Add("cancelled delivery damaged target");
        if (IsInstanceValid(_casterStats) && _casterStats.GetCurrent(StatType.Health) != _casterHealth)
            issues.Add("cancelled totem healed caster");
        if (SpellBarrier.Count != 0 || _barrier.Active || !_barrier.Ended) issues.Add("barrier still intercepts");
        if (_barrier.GetNode<StaticBody3D>("Solid").CollisionLayer != 0u) issues.Add("solid barrier still collides");
        if (_totem.GetNode<Hurtbox>("Hurtbox").CollisionLayer != 0u) issues.Add("totem still collides");
        if (_shot.CollisionLayer != 0u || _shot.Released != null) issues.Add("projectile still collides or can return");
        if (_ground.Caster != null || _zone.Caster != null || _barrier.Caster != null ||
            _totem.Caster != null || _totem.Target != null) issues.Add("delivery retains caster/target");
        if (_deliveries.Any(n => !n.IsQueuedForDeletion())) issues.Add("cancelled delivery not queued for cleanup");
        if (EventBus.Instance.SubscriberCount<GameLoadingEvent>() != _loadingBaseline ||
            EventBus.Instance.SubscriberCount<EntityDespawnedEvent>() != _despawnBaseline)
            issues.Add("delivery event subscriptions survived cancellation");
        return [.. issues];
    }

    public bool DeliveriesFreed() => _deliveries.Count == 5 && _deliveries.All(n => !IsInstanceValid(n));

    public void BeginPooling()
    {
        _pool = new NodePool<SpellProjectile>(() => new SpellProjectile { Released = ReturnShot });
        ReuseShot();
        _pooledId = _shot.GetInstanceId();
    }

    public void ReuseShot()
    {
        _shot = _pool!.Get();
        if (_pooledId != 0) ReusedSameShot &= _shot.GetInstanceId() == _pooledId;
        SpellLifetime.HostFor(_caster, _caster).AddChild(_shot);
        _shot.GlobalPosition = new Vector3(20f, 2f, 20f);
        SpellResource bolt = Spell("spell.ball_lightning");
        _shot.Launch(bolt, Packet(bolt), _caster, 0, Vector3.Right);
        Freeze(_shot);
    }

    public void ExpireShot() => _shot._PhysicsProcess(100d);

    public bool ShotReturned(int expected) => PoolReturns == expected && _pool!.Available == 1 &&
        IsInstanceValid(_shot) && !_shot.IsInsideTree() && ReusedSameShot;

    public bool QueuedReturnCancelled() => _shot.IsQueuedForDeletion() && _shot.CollisionLayer == 0u &&
        _shot.Released == null && PoolReturns == 2;

    public bool CancelledShotFreed() => !IsInstanceValid(_shot) && PoolReturns == 2 && _pool!.Available == 0;

    public void Finish()
    {
        _pool?.Clear();
        _pool = null;
        _pooledId = 0;
        _session.GetParent().RemoveChild(_session);
        _session.QueueFree();
    }

    private void ReturnShot(SpellProjectile shot)
    {
        PoolReturns++;
        _pool!.Return(shot);
    }

    private T Attach<T>(T delivery, Vector3 at) where T : Node3D
    {
        SpellLifetime.HostFor(_caster, _caster).AddChild(delivery);
        delivery.GlobalPosition = at;
        Freeze(delivery);
        _deliveries.Add(delivery);
        return delivery;
    }

    private static void Freeze(Node node)
    {
        node.SetProcess(false);
        node.SetPhysicsProcess(false);
    }

    private DamagePacket Packet(SpellResource spell) => new(spell.BaseDamage, spell.School,
        _caster, false, spell.PoiseDamage, HitKind.Spell, Unblockable: true);

    private static SpellResource Spell(string id) => SpellDatabase.Get(id) ??
        throw new InvalidOperationException($"Missing authored probe spell {id}");

    private static Entity Actor(string name, int team)
    {
        var actor = new Entity { Name = name };
        actor.AddChild(new StatsComponent { Name = "Stats", HealthRegen = 0f });
        actor.AddChild(new CombatComponent { Name = "Combat", Team = team });
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.5f },
            Position = new Vector3(0f, 0.5f, 0f) });
        actor.AddChild(hurtbox);
        return actor;
    }
}
