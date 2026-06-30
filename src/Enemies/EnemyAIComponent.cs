using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The decision-making brain for an <see cref="EnemyEntity"/>. A perception-driven
/// finite state machine (Idle → Patrol → Investigate → Combat → Retreat) that
/// reuses the shared <see cref="LocomotionComponent"/> to move and the
/// <see cref="MeleeWeaponComponent"/> to attack — the same systems the player
/// uses. Sight is a range + field-of-view cone gated by a line-of-sight raycast,
/// with a short-range proximity sense. Spotting the target broadcasts an
/// <see cref="EnemyAlertedEvent"/> so nearby allies converge (group coordination).
/// </summary>
[GlobalClass]
public partial class EnemyAIComponent : EntityComponent
{
    [ExportGroup("Perception")]
    [Export] public float VisionRange { get; set; } = 18f;
    [Export] public float FovDegrees { get; set; } = 110f;
    [Export] public float ProximityRange { get; set; } = 3f;
    [Export] public float AlertRadius { get; set; } = 14f;

    [ExportGroup("Behaviour")]
    [Export] public float AttackRange { get; set; } = 2.1f;
    [Export] public float RetreatHealthFraction { get; set; } = 0.25f;
    [Export] public float MaxRetreatTime { get; set; } = 3.5f;
    [Export] public float PatrolRadius { get; set; } = 6f;
    [Export] public float IdleDuration { get; set; } = 2.5f;
    [Export] public float InvestigateDuration { get; set; } = 6f;

    /// <summary>Seconds an enemy keeps hunting after being struck before it forgets (so it stands down
    /// once it's no longer hostile by reputation). Refreshed while actually in combat.</summary>
    [Export] public float ProvokeMemory { get; set; } = 12f;
    [Export] public float DespawnDelay { get; set; } = 4f;

    [ExportGroup("Caster")]
    /// <summary>Max range a caster will cast from; it closes the gap when the target is farther (29.5F).</summary>
    [Export] public float CastRange { get; set; } = 14f;

    /// <summary>If the target comes inside this, the caster kites — backs away while still casting.</summary>
    [Export] public float KiteDistance { get; set; } = 6f;

    /// <summary>Radius a support caster scans for a wounded ally to heal/buff.</summary>
    [Export] public float AllySupportRange { get; set; } = 12f;

    /// <summary>Heal an ally (or itself) whose health falls below this fraction.</summary>
    [Export] public float AllyHealThreshold { get; set; } = 0.6f;

    [ExportGroup("Level of Detail")]
    /// <summary>Beyond this distance from the player the AI ticks rarely (and casts no shadow).</summary>
    [Export] public float ActiveDistance { get; set; } = 45f;

    /// <summary>Seconds between ticks while sleeping (far from the player).</summary>
    [Export] public float SleepInterval { get; set; } = 0.5f;

    /// <summary>Seconds between line-of-sight raycasts; perception is cached in between.</summary>
    [Export] public float PerceptionInterval { get; set; } = 0.15f;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private MeleeWeaponComponent? _weapon;
    private SpellcastingComponent? _casting;
    private CombatComponent? _combat;
    private PlayerCharacter? _player;
    private MeshInstance3D? _mesh;
    private NavigationAgent3D? _agent;
    private string _factionId = string.Empty;
    private bool _provoked;
    private double _provokeTimer;

    private EnemyState _state = EnemyState.Idle;
    private double _stateTimer;
    private double _deathTimer;
    private bool _freed;
    private Vector3 _home;
    private Vector3 _lastKnownPos;
    private Vector3 _patrolTarget;

    // LOD bookkeeping.
    private double _sleepTimer;
    private double _perceptionTimer;
    private bool _cachedCanSee;
    private Vector3 _cachedSeenPos;
    private bool _shadowOn = true;

    // Reused line-of-sight query: perception fires every PerceptionInterval per enemy, so the
    // ray params + single-element exclude list are built once and only From/To change per cast.
    private PhysicsRayQueryParameters3D? _losQuery;
    private Godot.Collections.Array<Rid>? _losExclude;

    public EnemyState State => _state;

    protected override void OnInitialize()
    {
        if (Entity!.Body is not CharacterBody3D body)
        {
            Log.Error($"{nameof(EnemyAIComponent)} requires a CharacterEntity owner.");
            return;
        }

        _body = body;
        _stats = Entity.GetComponent<StatsComponent>();
        _weapon = Entity.GetComponent<MeleeWeaponComponent>();
        _casting = Entity.GetComponent<SpellcastingComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _mesh = _body.GetNodeOrNull<MeshInstance3D>("Mesh");
        _agent = _body.GetNodeOrNull<NavigationAgent3D>("NavAgent");
        _factionId = Entity.GetComponent<FactionComponent>()?.FactionId ?? string.Empty;
        _home = _body.GlobalPosition;
        _lastKnownPos = _home;

        EventBus.Instance?.Subscribe<EnemyAlertedEvent>(OnEnemyAlerted);
        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamaged);
        EnterState(EnemyState.Idle);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EnemyAlertedEvent>(OnEnemyAlerted);
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamaged);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        _perceptionTimer -= delta;

        // Level of detail: a live enemy far from the player ticks rarely and stops casting a
        // shadow. The dead state always runs so corpses still despawn on schedule.
        bool far = IsFarFromPlayer();
        SetShadow(!far);
        if (far && _state != EnemyState.Dead)
        {
            _sleepTimer -= delta;
            if (_sleepTimer > 0d)
            {
                return;
            }

            _sleepTimer = SleepInterval;
        }

        _stateTimer += delta;

        // Provoke memory: a struck enemy hunts the player, but forgets after a calm spell so it stands
        // down once reputation is no longer hostile (it never forgets mid-fight).
        if (_provoked)
        {
            if (_state == EnemyState.Combat)
            {
                _provokeTimer = ProvokeMemory;
            }
            else
            {
                _provokeTimer -= delta;
                if (_provokeTimer <= 0d)
                {
                    _provoked = false;
                }
            }
        }

        if (_state != EnemyState.Dead && (_stats == null || !_stats.IsAlive))
        {
            EnterState(EnemyState.Dead);
        }

        switch (_state)
        {
            case EnemyState.Idle:
                TickIdle(delta);
                break;
            case EnemyState.Patrol:
                TickPatrol(delta);
                break;
            case EnemyState.Investigate:
                TickInvestigate(delta);
                break;
            case EnemyState.Combat:
                TickCombat(delta);
                break;
            case EnemyState.Retreat:
                TickRetreat(delta);
                break;
            case EnemyState.Dead:
                TickDead(delta);
                break;
        }
    }

    // --- States -------------------------------------------------------------

    private void TickIdle(double delta)
    {
        Stand(delta);
        if (DetectAndEngage())
        {
            return;
        }

        if (_stateTimer >= IdleDuration)
        {
            EnterState(EnemyState.Patrol);
        }
    }

    private void TickPatrol(double delta)
    {
        if (DetectAndEngage())
        {
            return;
        }

        MoveTowards(_patrolTarget, delta, sprint: false, stopDistance: 0.6f);
        if (HorizontalDistance(_body.GlobalPosition, _patrolTarget) < 1f)
        {
            PickPatrolTarget();
        }
    }

    private void TickInvestigate(double delta)
    {
        PlayerCharacter? player = GetLivePlayer();
        if (player != null && CanSeePlayer(player, out Vector3 pos))
        {
            _lastKnownPos = pos;
            EnterState(EnemyState.Combat);
            return;
        }

        FaceTowards(_lastKnownPos);
        if (HorizontalDistance(_body.GlobalPosition, _lastKnownPos) > 1f)
        {
            MoveTowards(_lastKnownPos, delta, sprint: false, stopDistance: 0.8f);
        }
        else
        {
            Stand(delta);
            if (_stateTimer >= InvestigateDuration)
            {
                EnterState(EnemyState.Patrol);
            }
        }
    }

    private void TickCombat(double delta)
    {
        PlayerCharacter? player = GetLivePlayer();
        if (player == null)
        {
            EnterState(EnemyState.Idle);
            return;
        }

        // Standing down (e.g. reputation rose to neutral) ends the fight unless provoked.
        if (!PlayerIsTarget())
        {
            EnterState(EnemyState.Idle);
            return;
        }

        if (!CanSeePlayer(player, out Vector3 pos))
        {
            EnterState(EnemyState.Investigate);
            return;
        }

        _lastKnownPos = pos;

        if (LowHealth())
        {
            EnterState(EnemyState.Retreat);
            return;
        }

        // A caster holds the cast band and kites instead of charging into melee (Phase 29.5F).
        if (_casting != null)
        {
            TickCasterCombat(pos, delta);
            return;
        }

        FaceTowards(pos);
        float dist = HorizontalDistance(_body.GlobalPosition, pos);
        if (dist > AttackRange)
        {
            MoveTowards(pos, delta, sprint: true, stopDistance: AttackRange * 0.85f);
        }
        else
        {
            Stand(delta);
            _weapon?.TryAttack();
        }
    }

    private void TickRetreat(double delta)
    {
        PlayerCharacter? player = GetLivePlayer();
        Vector3 threat = player != null ? player.GlobalPosition : _lastKnownPos;

        Vector3 away = _body.GlobalPosition - threat;
        away.Y = 0f;
        Vector3 fleeTarget = away.LengthSquared() > 0.01f
            ? _body.GlobalPosition + (away.Normalized() * 5f)
            : _home;

        MoveTowards(fleeTarget, delta, sprint: true, stopDistance: 0.1f);
        FaceTowards(threat);

        // A wounded caster heals/wards itself (and lobs spells) as it falls back (Phase 29.5F).
        if (_casting != null)
        {
            TryCasterCast();
        }

        if (_stateTimer >= MaxRetreatTime)
        {
            EnterState(player != null ? EnemyState.Combat : EnemyState.Investigate);
        }
    }

    // --- Caster behaviour (Phase 29.5F) -------------------------------------

    /// <summary>Caster combat: hold the cast band (approach when too far, kite when too close), face the
    /// target so the cast aims true, and fire whatever's ready. Reuses the player's
    /// <see cref="SpellcastingComponent"/> — no parallel casting system.</summary>
    private void TickCasterCombat(Vector3 targetPos, double delta)
    {
        FaceTowards(targetPos);
        float dist = HorizontalDistance(_body.GlobalPosition, targetPos);
        switch (CasterDecision.Move(dist, KiteDistance, CastRange))
        {
            case CasterMove.Kite:
                Vector3 away = _body.GlobalPosition - targetPos;
                away.Y = 0f;
                Vector3 flee = away.LengthSquared() > 0.01f
                    ? _body.GlobalPosition + (away.Normalized() * 5f)
                    : _home;
                MoveTowards(flee, delta, sprint: true, stopDistance: 0.1f);
                break;
            case CasterMove.Approach:
                MoveTowards(targetPos, delta, sprint: false, stopDistance: CastRange * 0.9f);
                break;
            default:
                Stand(delta);
                break;
        }

        TryCasterCast();
    }

    /// <summary>One cast action per tick, by priority: heal/buff a wounded ally, else attack, else ward
    /// itself. Per-spell cooldowns naturally pace it. Returns once something is cast.</summary>
    private void TryCasterCast()
    {
        if (_casting == null)
        {
            return;
        }

        // 1. Support: heal the most-wounded ally (or itself) that has fallen below the heal threshold.
        SpellResource? heal = ReadySupport(healing: true);
        if (heal != null && FindWoundedAlly() is { } ally && _casting.TryCastSupportOn(ally, heal))
        {
            return;
        }

        // 2. Offensive: the hardest-hitting ready damage spell, aimed down the body's facing.
        SpellResource? attack = ReadyOffensive();
        if (attack != null && _casting.TryCastById(attack.Id))
        {
            return;
        }

        // 3. Ward itself when nothing better to do and the buff isn't already up.
        SpellResource? ward = ReadySupport(healing: false);
        if (ward != null && Entity != null && !HasStatus(Entity, ward.StatusEffectId))
        {
            _casting.TryCastSupportOn(Entity, ward);
        }
    }

    /// <summary>The strongest ready offensive (non-Self, damaging) spell the caster knows, or null.</summary>
    private SpellResource? ReadyOffensive()
    {
        SpellResource? best = null;
        foreach (SpellResource spell in _casting!.Spells)
        {
            if (spell.Delivery != SpellDelivery.Self && spell.BaseDamage > 0f && _casting.CanCast(spell) &&
                (best == null || spell.BaseDamage > best.BaseDamage))
            {
                best = spell;
            }
        }

        return best;
    }

    /// <summary>A ready Self-delivery support spell: a heal (<paramref name="healing"/> true) or a
    /// beneficial ward (false), or null when none is castable.</summary>
    private SpellResource? ReadySupport(bool healing)
    {
        foreach (SpellResource spell in _casting!.Spells)
        {
            bool isHeal = spell.Healing > 0f;
            if (spell.Delivery == SpellDelivery.Self && isHeal == healing && _casting.CanCast(spell) &&
                (healing || spell.HasStatusEffect))
            {
                return spell;
            }
        }

        return null;
    }

    /// <summary>The most-wounded ally (or itself) within <see cref="AllySupportRange"/> on the caster's
    /// team whose health is below <see cref="AllyHealThreshold"/>, or null when none needs healing.</summary>
    private IEntity? FindWoundedAlly()
    {
        int team = _combat?.Team ?? 0;
        IEntity? best = null;
        float lowest = AllyHealThreshold;

        foreach (Node node in GetTree().GetNodesInGroup(Quests.ObjectiveLocator.EnemyGroup))
        {
            if (node is not Node3D body ||
                HorizontalDistance(_body.GlobalPosition, body.GlobalPosition) > AllySupportRange ||
                EntityNode.FindOwner(node) is not { } ally ||
                ally.GetComponent<CombatComponent>()?.Team != team)
            {
                continue;
            }

            StatsComponent? stats = ally.GetComponent<StatsComponent>();
            if (stats is not { IsAlive: true })
            {
                continue;
            }

            float fraction = stats.GetNormalized(StatType.Health);
            if (fraction < lowest)
            {
                lowest = fraction;
                best = ally;
            }
        }

        return best;
    }

    private static bool HasStatus(IEntity entity, string statusId) =>
        !string.IsNullOrEmpty(statusId) && entity.GetComponent<StatusEffectsComponent>()?.Has(statusId) == true;

    private void TickDead(double delta)
    {
        Stand(delta);
        if (_freed)
        {
            return;
        }

        _deathTimer -= delta;
        if (_deathTimer <= 0d)
        {
            _freed = true;
            ((Node)Entity!.Body).QueueFree();
        }
    }

    // --- Perception & coordination -----------------------------------------

    private bool DetectAndEngage()
    {
        if (!PlayerIsTarget())
        {
            return false;
        }

        PlayerCharacter? player = GetLivePlayer();
        if (player != null && CanSeePlayer(player, out Vector3 pos))
        {
            _lastKnownPos = pos;
            EventBus.Instance?.Publish(new EnemyAlertedEvent(Entity!, pos));
            EnterState(EnemyState.Combat);
            return true;
        }

        return false;
    }

    /// <summary>Perception, throttled: the (relatively costly) sight check — FOV + line-of-sight
    /// raycast — runs at most once per <see cref="PerceptionInterval"/> and is cached between,
    /// so a crowd of enemies doesn't raycast every physics frame.</summary>
    private bool CanSeePlayer(PlayerCharacter player, out Vector3 seenPosition)
    {
        if (_perceptionTimer <= 0d)
        {
            _cachedCanSee = ComputeCanSeePlayer(player, out _cachedSeenPos);
            _perceptionTimer = PerceptionInterval;
        }

        seenPosition = _cachedSeenPos;
        return _cachedCanSee;
    }

    private bool ComputeCanSeePlayer(PlayerCharacter player, out Vector3 seenPosition)
    {
        Vector3 selfPos = _body.GlobalPosition;
        Vector3 playerPos = player.GlobalPosition;
        seenPosition = playerPos;

        Vector3 flat = playerPos - selfPos;
        flat.Y = 0f;
        float dist = flat.Length();
        if (dist > VisionRange || dist < 0.001f)
        {
            return dist <= VisionRange; // standing on the player still counts as seen
        }

        // Outside the proximity bubble the target must be within the view cone.
        if (dist > ProximityRange)
        {
            Vector3 forward = -_body.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (!EnemyPerception.InViewCone(forward, flat, FovDegrees))
            {
                return false;
            }
        }

        return HasLineOfSight(player, selfPos + (Vector3.Up * 1.6f), playerPos + (Vector3.Up * 1.2f));
    }

    private bool HasLineOfSight(PlayerCharacter player, Vector3 from, Vector3 to)
    {
        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;

        // Build the query + exclude list once; the excluded RID (this body) never changes.
        if (_losQuery == null)
        {
            _losExclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
            _losQuery = PhysicsRayQueryParameters3D.Create(from, to);
            _losQuery.Exclude = _losExclude;
        }

        _losQuery.From = from;
        _losQuery.To = to;

        Godot.Collections.Dictionary hit = space.IntersectRay(_losQuery);
        if (hit.Count == 0)
        {
            return true; // nothing in the way
        }

        // Visible only if the first thing the ray hits is the player.
        if (hit["collider"].AsGodotObject() is Node node)
        {
            return ReferenceEquals(EntityNode.FindOwner(node), player);
        }

        return true;
    }

    /// <summary>
    /// Whether this actor currently treats the player as a target. A faction member
    /// engages only while the player's standing with its faction is hostile (or it has
    /// been provoked by a direct attack); an unfactioned actor is hostile by default.
    /// </summary>
    private bool PlayerIsTarget()
    {
        if (_provoked || string.IsNullOrEmpty(_factionId))
        {
            return true;
        }

        ReputationComponent? reputation = GetPlayer()?.GetComponent<ReputationComponent>();
        return reputation == null || reputation.IsHostile(_factionId);
    }

    private void OnDamaged(DamageDealtEvent e)
    {
        // Being struck by the player is self-defence grounds regardless of standing.
        if (Entity == null || !ReferenceEquals(e.Target, Entity) || e.Source is not PlayerCharacter attacker)
        {
            return;
        }

        _provoked = true;
        _provokeTimer = ProvokeMemory;
        if (_state is EnemyState.Idle or EnemyState.Patrol or EnemyState.Investigate)
        {
            _lastKnownPos = attacker.GlobalPosition;
            EnterState(EnemyState.Combat);
        }
    }

    private void OnEnemyAlerted(EnemyAlertedEvent e)
    {
        if (ReferenceEquals(e.Source, Entity) || _body == null)
        {
            return;
        }

        if (_state is EnemyState.Idle or EnemyState.Patrol &&
            HorizontalDistance(_body.GlobalPosition, e.Position) <= AlertRadius)
        {
            _lastKnownPos = e.Position;
            EnterState(EnemyState.Investigate);
        }
    }

    // --- Movement helpers ---------------------------------------------------

    private void MoveTowards(Vector3 target, double delta, bool sprint, float stopDistance)
    {
        // Steer toward the next navmesh path corner (Phase 27A) when one is available; arrival is
        // judged against the FINAL target, never the corner, so the actor doesn't stop short at bends.
        Vector3 corner = NextPathPoint(target);
        Vector3 toCorner = corner - _body.GlobalPosition;
        toCorner.Y = 0f;
        float cornerDist = toCorner.Length();
        float finalDist = HorizontalDistance(_body.GlobalPosition, target);
        Vector3 wish = PathSteering.ShouldSteer(cornerDist, finalDist, stopDistance)
            ? toCorner.Normalized()
            : Vector3.Zero;
        GetLocomotion()?.Move(delta, wish, sprint, jump: false);
    }

    /// <summary>
    /// The next waypoint to steer toward. With a baked navmesh under the agent this is the next path
    /// corner around obstacles; with no navmesh (the procedural sandbox) or an unreachable target the
    /// path query yields nothing, so we fall back to steering straight at the target — the pre-27A
    /// behaviour. Re-targets the agent only when the goal actually moves, to avoid needless repaths.
    /// </summary>
    private Vector3 NextPathPoint(Vector3 target)
    {
        if (_agent == null)
        {
            return target;
        }

        if (_agent.TargetPosition.DistanceSquaredTo(target) > 0.01f)
        {
            _agent.TargetPosition = target;
        }

        return _agent.IsTargetReachable() ? _agent.GetNextPathPosition() : target;
    }

    private void Stand(double delta)
    {
        GetLocomotion()?.Move(delta, Vector3.Zero, sprint: false, jump: false);
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) > 0.0004f)
        {
            _body.LookAt(flat, Vector3.Up);
        }
    }

    private LocomotionComponent? GetLocomotion()
    {
        return Entity?.GetComponent<LocomotionComponent>();
    }

    // --- Misc helpers -------------------------------------------------------

    private void EnterState(EnemyState next)
    {
        if (_state == next)
        {
            return;
        }

        _state = next;
        _stateTimer = 0d;

        switch (next)
        {
            case EnemyState.Patrol:
                PickPatrolTarget();
                break;
            case EnemyState.Dead:
                _deathTimer = DespawnDelay;
                break;
        }

        EventBus.Instance?.Publish(new EnemyStateChangedEvent(Entity!, next));
    }

    private void PickPatrolTarget()
    {
        float angle = GD.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(GD.Randf()) * PatrolRadius;
        _patrolTarget = _home + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private bool LowHealth()
    {
        return _stats != null && _stats.GetNormalized(StatType.Health) < RetreatHealthFraction;
    }

    /// <summary>True when no player exists or the player is beyond <see cref="ActiveDistance"/>.</summary>
    private bool IsFarFromPlayer()
    {
        PlayerCharacter? player = GetPlayer();
        if (player == null)
        {
            return true;
        }

        return _body.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > ActiveDistance * ActiveDistance;
    }

    private void SetShadow(bool on)
    {
        if (_mesh == null || on == _shadowOn)
        {
            return;
        }

        _shadowOn = on;
        _mesh.CastShadow = on
            ? GeometryInstance3D.ShadowCastingSetting.On
            : GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private PlayerCharacter? GetLivePlayer()
    {
        PlayerCharacter? player = GetPlayer();
        if (player == null)
        {
            return null;
        }

        StatsComponent? stats = player.GetComponent<StatsComponent>();
        return stats == null || stats.IsAlive ? player : null;
    }

    private PlayerCharacter? GetPlayer()
    {
        if (_player != null && IsInstanceValid(_player))
        {
            return _player;
        }

        _player = null;
        if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out PlayerCharacter found))
        {
            _player = found;
        }

        return _player;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.Y = 0f;
        b.Y = 0f;
        return a.DistanceTo(b);
    }
}
