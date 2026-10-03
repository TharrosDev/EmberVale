using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    private CharacterEntity _caster = null!;
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
    private int _deathBaseline;
    private int _casterLoadingSubscriptions;
    private int _casterDespawnSubscriptions;
    private int _casterDeathSubscriptions;
    private ulong _pooledId;
    private readonly List<StatsComponent> _burstTargets = new();
    private SpellResource? _burstSpell;
    private Node3D? _burstDelivery;
    private SpellcastingComponent? _burstCasting;
    private string _burstSource = string.Empty;
    private string _burstMode = string.Empty;
    private int _burstDamageEvents;
    private int _burstHitEvents;
    public int PoolReturns { get; private set; }
    public bool ReusedSameShot { get; private set; }

    public bool DefaultAttributesAreIndependent()
    {
        using AttributeSet first = AttributeSet.CreateDefault();
        using AttributeSet second = AttributeSet.CreateDefault();
        first.Health = 1f;
        first.Armor = 99f;
        return !ReferenceEquals(first, second) && first.GetInstanceId() != second.GetInstanceId() &&
            second.Health == 100f && second.Armor == 0f;
    }

    public bool FixtureDefaultsRetained() => _casterStats.Attributes is { } casterAttributes &&
        _targetStats.Attributes is { } targetAttributes &&
        IsInstanceValid(casterAttributes) && IsInstanceValid(targetAttributes) &&
        !ReferenceEquals(casterAttributes, targetAttributes) &&
        casterAttributes.Health == 100f && targetAttributes.Health == 100f &&
        casterAttributes.Armor == 0f && targetAttributes.Armor == 0f;

    public void Begin(Node parent)
    {
        _deliveries.Clear();
        _session = new GameSession();
        parent.AddChild(_session); // no Build: production ownership without world streaming
        _caster = CharacterActor("LifetimeCaster", 0);
        int loadingBefore = EventBus.Instance.SubscriberCount<GameLoadingEvent>();
        int despawnBefore = EventBus.Instance.SubscriberCount<EntityDespawnedEvent>();
        int deathBefore = EventBus.Instance.SubscriberCount<EntityDiedEvent>();
        _session.Players.AddChild(_caster);
        _casterLoadingSubscriptions = EventBus.Instance.SubscriberCount<GameLoadingEvent>() - loadingBefore;
        _casterDespawnSubscriptions = EventBus.Instance.SubscriberCount<EntityDespawnedEvent>() - despawnBefore;
        _casterDeathSubscriptions = EventBus.Instance.SubscriberCount<EntityDiedEvent>() - deathBefore;
        _caster.Position = new Vector3(0f, 0f, 4f);
        Entity target = Actor("LifetimeTarget", 1);
        _session.World.AddChild(target);
        _casterStats = _caster.GetComponent<StatsComponent>()!;
        _targetStats = target.GetComponent<StatsComponent>()!;
        _casterStats.SetCurrent(StatType.Health, _casterStats.GetMax(StatType.Health) * 0.5f);
        _loadingBaseline = EventBus.Instance.SubscriberCount<GameLoadingEvent>();
        _despawnBaseline = EventBus.Instance.SubscriberCount<EntityDespawnedEvent>();
        _deathBaseline = EventBus.Instance.SubscriberCount<EntityDiedEvent>();
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

    public void DeathAndRespawn()
    {
        EventBus.Instance.Subscribe<EntityDiedEvent>(ReviveCaster);
        try
        {
            _casterStats.ApplyDamage(float.MaxValue);
        }
        finally
        {
            EventBus.Instance.Unsubscribe<EntityDiedEvent>(ReviveCaster);
        }
        _casterHealth = _casterStats.GetCurrent(StatType.Health);
    }

    private void ReviveCaster(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, _caster))
        {
            _casterStats.RefillResources();
        }
    }

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
        // Detaching the caster also tears down its status-component subscribers immediately.
        // Account for only that independently measured fixture footprint; every delivery-owned
        // subscriber must still be removed exactly, even before deferred delivery cleanup.
        bool casterDetached = !IsInstanceValid(_caster) || !_caster.IsInsideTree();
        if (EventBus.Instance.SubscriberCount<GameLoadingEvent>() !=
                _loadingBaseline - (casterDetached ? _casterLoadingSubscriptions : 0) ||
            EventBus.Instance.SubscriberCount<EntityDespawnedEvent>() !=
                _despawnBaseline - (casterDetached ? _casterDespawnSubscriptions : 0) ||
            EventBus.Instance.SubscriberCount<EntityDiedEvent>() !=
                _deathBaseline - (casterDetached ? _casterDeathSubscriptions : 0))
            issues.Add("delivery event subscriptions survived cancellation");
        return [.. issues];
    }

    public bool DeliveriesFreed() => _deliveries.Count == 5 && _deliveries.All(n => !IsInstanceValid(n));

    public void PrepareBurstCancellation(string source, string mode)
    {
        _burstSource = source;
        _burstMode = mode;
        _burstDamageEvents = 0;
        _burstHitEvents = 0;
        _burstTargets.Clear();
        _burstTargets.Add(_targetStats);
        foreach (float x in new[] { -1f, 1f })
        {
            Entity target = Actor("BurstTarget", 1);
            _session.World.AddChild(target);
            target.GlobalPosition = new Vector3(x, 0f, 0f);
            _burstTargets.Add(target.GetComponent<StatsComponent>()!);
        }

        _burstSpell = new SpellResource
        {
            Id = "spell.probe_cancelled_burst",
            School = DamageType.Frost,
            BaseDamage = 10f,
            StatusEffectId = StatusIds.Chill,
            ImpactRadius = 4f,
            ProjectileSpeed = 10f,
            Range = 30f,
            AffectsCaster = true,
            DashDistance = 8f,
            DashHitRadius = 2f,
        };
        _burstDelivery = source switch
        {
            "projectile" => Attach(new SpellProjectile(), new Vector3(0f, 0.5f, 0f)),
            "ground" => Attach(new SpellGround { Spell = _burstSpell, Packet = Packet(_burstSpell),
                Caster = _caster, CasterTeam = 0, Delay = 0f, Radius = 4f }, Vector3.Zero),
            "zone" => Attach(new SpellZone { Spell = _burstSpell, Packet = Packet(_burstSpell),
                Caster = _caster, CasterTeam = 0, Radius = 4f, Duration = 4f }, Vector3.Zero),
            "direct" or "cone" or "dash" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        if (_burstDelivery is SpellProjectile projectile)
        {
            projectile.Launch(_burstSpell, Packet(_burstSpell), _caster, 0, Vector3.Right);
            Freeze(projectile);
        }
        if (source == "dash")
        {
            _burstSpell.Delivery = SpellDelivery.Dash;
            _burstCasting = new SpellcastingComponent { Name = "DashCasting" };
            _caster.AddChild(_burstCasting);
            Freeze(_burstCasting);
        }

        EventBus.Instance.Subscribe<DamageDealtEvent>(OnBurstDamage);
        EventBus.Instance.Subscribe<SpellHitEvent>(OnBurstHit);
    }

    public string[] ResolveBurstCancellation()
    {
        SpellResource spell = _burstSpell!;
        switch (_burstDelivery)
        {
            case SpellProjectile projectile:
                projectile._PhysicsProcess(0d);
                break;
            case SpellGround ground:
                ground._Process(0d);
                break;
            case SpellZone zone:
                zone._Process(0d);
                break;
            default:
                if (_burstSource == "dash")
                {
                    MethodInfo castDash = typeof(SpellcastingComponent).GetMethod("CastDash",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    castDash.Invoke(_burstCasting, [spell, 0, 1f, 0f]);
                }
                else if (_burstSource == "cone")
                {
                    SpellResolver.Sweep(_caster, spell, Packet(spell), _caster, 0,
                        new Vector3(0f, 0.5f, 2f), Vector3.Forward, 4f, 120f);
                }
                else
                {
                    SpellResolver.Detonate(_caster, spell, Packet(spell), _caster, 0,
                        new Vector3(0f, 0.5f, 0f), 4f);
                }
                break;
        }

        var issues = new List<string>();
        bool cancelled = _burstMode != "normal";
        int expected = cancelled ? 1 : 3;
        int damaged = _burstTargets.Count(s => s.GetCurrent(StatType.Health) < s.GetMax(StatType.Health));
        int afflicted = _burstTargets.Count(s => s.Entity!.GetComponent<StatusEffectsComponent>()!.Has(StatusIds.Chill));
        if (_burstDamageEvents != expected || damaged != expected)
            issues.Add($"expected {expected} damaged targets/events, got {damaged}/{_burstDamageEvents}");
        if (_burstHitEvents != expected)
            issues.Add($"first successful hit lost feedback, or later cancelled hits published: {_burstHitEvents}");
        if (afflicted != (cancelled ? 0 : 3))
            issues.Add($"expected {(cancelled ? 0 : 3)} status riders, got {afflicted}");
        bool casterCaught = _caster.GetComponent<StatusEffectsComponent>()!.Has(StatusIds.Chill);
        if (casterCaught != (!cancelled && _burstSource is not "cone" and not "dash"))
            issues.Add("caster-catching status did not respect burst cancellation");
        int stunned = _burstTargets.Count(s => s.Entity!.GetComponent<StatusEffectsComponent>()!.Has(StatusIds.Stunned));
        int expectedStuns = !cancelled && _burstSource == "dash" ? 1 : 0;
        if (stunned != expectedStuns)
            issues.Add($"expected {expectedStuns} final dash stuns, got {stunned}");
        if (cancelled && _burstDelivery != null && !_burstDelivery.IsQueuedForDeletion())
            issues.Add("persistent delivery remained live after first-hit cancellation");
        if (_burstMode == "respawn" && !_casterStats.IsAlive)
            issues.Add("immediate-respawn control did not revive the caster");
        GD.Print($"burst {_burstSource}/{_burstMode}: damage {damaged}, riders {afflicted}, hit events {_burstHitEvents}, stuns {stunned}");
        return [.. issues];
    }

    private void OnBurstDamage(DamageDealtEvent e)
    {
        if (!_burstTargets.Any(s => ReferenceEquals(s.Entity, e.Target)) || ++_burstDamageEvents != 1)
        {
            return;
        }

        switch (_burstMode)
        {
            case "load":
                PreLoad();
                break;
            case "death":
            case "respawn":
                _casterStats.ApplyDamage(float.MaxValue);
                if (_burstMode == "respawn")
                {
                    _casterStats.RefillResources();
                }
                break;
        }
    }

    private void OnBurstHit(SpellHitEvent e)
    {
        if (e.SpellId == _burstSpell?.Id)
        {
            _burstHitEvents++;
        }
    }

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
        EventBus.Instance.Unsubscribe<DamageDealtEvent>(OnBurstDamage);
        EventBus.Instance.Unsubscribe<SpellHitEvent>(OnBurstHit);
        _burstTargets.Clear();
        _burstSpell?.Dispose();
        _burstSpell = null;
        _burstDelivery = null;
        _burstCasting = null;
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
        AddActorComponents(actor, team);
        return actor;
    }

    private static CharacterEntity CharacterActor(string name, int team)
    {
        var actor = new CharacterEntity { Name = name };
        AddActorComponents(actor, team);
        return actor;
    }

    private static void AddActorComponents(Node3D actor, int team)
    {
        actor.AddChild(new StatsComponent { Name = "Stats", HealthRegen = 0f });
        actor.AddChild(new CombatComponent { Name = "Combat", Team = team });
        actor.AddChild(new StatusEffectsComponent { Name = "Status" });
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.5f },
            Position = new Vector3(0f, 0.5f, 0f) });
        actor.AddChild(hurtbox);
    }
}
