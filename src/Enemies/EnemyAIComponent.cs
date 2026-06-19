using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
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
    [Export] public float DespawnDelay { get; set; } = 4f;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private MeleeWeaponComponent? _weapon;
    private PlayerCharacter? _player;

    private EnemyState _state = EnemyState.Idle;
    private double _stateTimer;
    private double _deathTimer;
    private bool _freed;
    private Vector3 _home;
    private Vector3 _lastKnownPos;
    private Vector3 _patrolTarget;

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
        _home = _body.GlobalPosition;
        _lastKnownPos = _home;

        EventBus.Instance?.Subscribe<EnemyAlertedEvent>(OnEnemyAlerted);
        EnterState(EnemyState.Idle);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EnemyAlertedEvent>(OnEnemyAlerted);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        _stateTimer += delta;

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

        if (_stateTimer >= MaxRetreatTime)
        {
            EnterState(player != null ? EnemyState.Combat : EnemyState.Investigate);
        }
    }

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

    private bool CanSeePlayer(PlayerCharacter player, out Vector3 seenPosition)
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
            if (forward.LengthSquared() > 0.0001f)
            {
                float dot = forward.Normalized().Dot(flat / dist);
                float cosHalfFov = Mathf.Cos(Mathf.DegToRad(FovDegrees * 0.5f));
                if (dot < cosHalfFov)
                {
                    return false;
                }
            }
        }

        return HasLineOfSight(player, selfPos + (Vector3.Up * 1.6f), playerPos + (Vector3.Up * 1.2f));
    }

    private bool HasLineOfSight(PlayerCharacter player, Vector3 from, Vector3 to)
    {
        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };

        Godot.Collections.Dictionary hit = space.IntersectRay(query);
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
        Vector3 toTarget = target - _body.GlobalPosition;
        toTarget.Y = 0f;
        float dist = toTarget.Length();
        Vector3 wish = dist > stopDistance && dist > 0.01f ? toTarget.Normalized() : Vector3.Zero;
        GetLocomotion()?.Move(delta, wish, sprint, jump: false);
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
