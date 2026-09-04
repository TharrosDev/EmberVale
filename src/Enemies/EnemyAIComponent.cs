using Embervale.Combat.Actions;
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
/// <see cref="CharacterActionComponent"/> to attack — the same systems the player
/// uses. Sight is a range + field-of-view cone gated by a line-of-sight raycast,
/// with a short-range proximity sense. Spotting the target broadcasts an
/// <see cref="EnemyAlertedEvent"/> so nearby allies converge (group coordination).
///
/// <b>Phase 34A:</b> every tuning knob moved out to an <see cref="AIProfileResource"/> resolved by
/// <see cref="ProfileId"/>. The brain stayed one class — "ranged", "shielded", "pack-flanker",
/// "coward" and "ambusher" are branches gated on profile numbers, not subclasses, so an archetype
/// can combine them freely and a designer retunes any of it without a rebuild.
/// </summary>
[GlobalClass]
public partial class EnemyAIComponent : EntityComponent
{
    /// <summary>The default personality: straight-ahead melee, i.e. the pre-34A behaviour.</summary>
    public const string DefaultProfileId = "ai.brute";

    /// <summary>How long an actor tries for one patrol point before choosing another.</summary>
    private const double PatrolGiveUpSeconds = 12d;

    /// <summary>Which <see cref="AIProfileResource"/> drives this actor (see <c>data/ai_profiles/</c>).
    /// An unknown id falls back to a default brute profile with a warning, so a content typo degrades
    /// to "fights plainly" rather than a dead brain.
    ///
    /// ⚠️ <b>Assigning this after the actor is live re-resolves the brain, and it has to.</b> This was
    /// an auto-property, and the profile is resolved exactly once in <see cref="OnInitialize"/> and
    /// cached in <c>_profile</c> — so <c>BossController</c>'s phase-change write (from
    /// <c>BossPhaseResource.AiProfileId</c>) landed on a field nothing read again and changed nothing.
    /// <c>ContentValidator</c> checks that field, so the first boss to author it would have got a
    /// green validator, no warning, and no behaviour.
    /// </summary>
    [Export] public string ProfileId
    {
        get => _profileId;
        set
        {
            _profileId = value;

            // Only once the actor is live: Godot writes exported properties during scene load, well
            // before the databases are populated, and OnInitialize resolves it properly anyway.
            if (_profileResolved)
            {
                _profile = ResolveProfile(allowInline: false);
            }
        }
    }

    private string _profileId = DefaultProfileId;
    private bool _profileResolved;

    /// <summary>Directly-assigned profile, for tests and for scenes that would rather inline it than
    /// go through the database. Wins over <see cref="ProfileId"/> when set.</summary>
    [Export] public AIProfileResource? Profile { get; set; }

    private AIProfileResource _profile = AIProfileResource.CreateDefault();

    /// <summary>The resolved profile driving this actor.</summary>
    public AIProfileResource ActiveProfile => _profile;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private CharacterActionComponent? _weapon;
    private SpellcastingComponent? _casting;
    private CombatComponent? _combat;
    private PlayerCharacter? _player;
    private MeshInstance3D? _mesh;
    private string _factionId = string.Empty;

    private EnemyState _state = EnemyState.Idle;
    private double _stateTimer;
    private double _deathTimer;
    private bool _freed;
    private Vector3 _home;
    private Vector3 _lastKnownPos;
    private Vector3 _patrolTarget;

    /// <summary>Seconds spent on the current patrol target. See <see cref="TickPatrol"/>.</summary>
    private double _patrolElapsed;

    // Guard rhythm (shielded profiles) + this actor's slot in the pack fan-out.
    private double _combatElapsed;
    private int _packSlot;

    /// <summary>Level of detail: when this actor may next think, and how much real time it has
    /// slept through since it last did. See <see cref="AiLodClock"/> for the six-minute provoke
    /// memory that made the banking necessary.</summary>
    private AiLodClock _lod;

    /// <summary>Seconds before a wounded actor may break off again. Without it, the re-engage at the
    /// end of a retreat walks straight back into the same low-health check that started it.</summary>
    private double _retreatCooldown;


    private bool _shadowOn = true;

    /// <summary>Navmesh steering, arrival and facing — shared with the companion brain, which used
    /// to carry its own drifted copy of the same three-answer rule.</summary>
    private AiNavigator _nav = null!;

    /// <summary>Sight, faction standing and provocation memory — the actor's whole picture of the
    /// world, cached at the profile's perception interval rather than recomputed per frame.</summary>
    private EnemySenses _senses = null!;

    /// <summary>How a standoff fighter fights. Built for every actor but only ticked by one whose
    /// profile stands off.</summary>
    private EnemyCasterTactics _tactics = null!;

    // This frame's delta, cached for FaceTowards' turn-rate slew (35A).
    private double _frameDelta;

    // Null on everything that walks (35B). Present, it owns the vertical axis; this brain still owns
    // the horizontal one, which is why flight needed no aerial state in the FSM below.
    private FlightComponent? _flight;

    /// <summary>True while this actor is too high for its melee to reach the ground.</summary>
    private bool Airborne => _flight is { IsOutOfMeleeReach: true };

    public EnemyState State => _state;

    protected override void OnInitialize()
    {
        if (Entity!.Body is not CharacterBody3D body)
        {
            Log.Error($"{nameof(EnemyAIComponent)} requires a CharacterEntity owner.");
            return;
        }

        _profile = ResolveProfile(allowInline: true);
        _profileResolved = true;

        // A stable per-instance slot, so members of a pack fan to different sides of the target and
        // keep that side for their whole life rather than jittering between approaches each tick.
        _packSlot = (int)(body.GetInstanceId() % 5UL);

        _body = body;
        _stats = Entity.GetComponent<StatsComponent>();
        _weapon = Entity.GetComponent<CharacterActionComponent>();
        _casting = Entity.GetComponent<SpellcastingComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _flight = Entity.GetComponent<FlightComponent>();
        _mesh = _body.GetNodeOrNull<MeshInstance3D>("Mesh");
        _nav = new AiNavigator(Entity, _body, _body.GetNodeOrNull<NavigationAgent3D>("NavAgent"));
        _tactics = new EnemyCasterTactics(Entity, _body, _nav, GetTree());
        _senses = new EnemySenses(Entity, _body);
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
        _senses?.Dispose();
    }

    /// <summary>An inline <see cref="Profile"/> wins; otherwise the id is looked up in the database.
    /// A miss warns once and falls back to a brute so the actor still fights.</summary>
    /// <param name="allowInline">False for a runtime <see cref="ProfileId"/> write. An authored
    /// inline profile is this node's starting personality, but a later assignment is a deliberate
    /// instruction from something that knows more (a boss entering its second phase) — so the id
    /// wins there, rather than the swap being silently ignored on any actor that inlines a profile.
    /// </param>
    private AIProfileResource ResolveProfile(bool allowInline) =>
        Resolve(ProfileId, allowInline ? Profile : null);

    /// <summary>The one answer to "which profile drives this actor", so nothing else has to
    /// re-derive it and get a different one. <c>EnemyArchetypeFactory</c> is the caller that made
    /// this static: it decides whether to attach a <see cref="FlightComponent"/> from the profile's
    /// takeoff range, and it was reading <c>AIProfileDatabase</c> directly — which skips
    /// <paramref name="inline"/> entirely, so an actor whose personality is inlined would fly by one
    /// answer and walk by the other. The entity is still detached while the factory builds it, so
    /// <see cref="ActiveProfile"/> is not usable there (<c>OnInitialize</c> has not run and the field
    /// is still the default) — reading it would look right and return the wrong profile.</summary>
    internal static AIProfileResource Resolve(string profileId, AIProfileResource? inline)
    {
        if (inline != null)
        {
            return inline;
        }

        if (AIProfileDatabase.Get(profileId) is { } found)
        {
            return found;
        }

        Log.Warn($"AI profile '{profileId}' is not registered; falling back to '{DefaultProfileId}'.");
        return AIProfileResource.CreateDefault();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        _senses.TickPerception(delta);
        // Cached so FaceTowards can slew at a profile's turn rate without threading delta through
        // the four state ticks that call it (35A).
        _frameDelta = delta;

        // Level of detail: a live enemy far from the player ticks rarely and stops casting a
        // shadow. The dead state always runs so corpses still despawn on schedule.
        bool far = IsFarFromPlayer();
        SetShadow(!far);
        if (far && _state != EnemyState.Dead && _lod.ShouldSleep(delta, _profile.SleepInterval))
        {
            return;
        }

        // Real time since this brain last thought, including anything it slept through. Wall-clock
        // timers (state duration, provoke memory, retreat cooldown) use it; movement and turn slew
        // keep using `delta`, because stepping a sleeping actor by half a second would teleport it.
        double wall = _lod.ConsumeWallSeconds(delta);

        _stateTimer += wall;
        _retreatCooldown -= wall;
        _tactics.TickTimers(wall);

        // Provoke memory: a struck enemy hunts the player, but forgets after a calm spell so it
        // stands down once reputation is no longer hostile (it never forgets mid-fight).
        _senses.TickProvocation(wall, _state == EnemyState.Combat, _profile.ProvokeMemory);

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
            case EnemyState.Returning:
                TickReturning(delta);
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

        // An ambusher holds its spot indefinitely — patrolling would walk it out of its own trap.
        if (!_profile.IsAmbusher && _stateTimer >= _profile.IdleDuration)
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
            return;
        }

        // ⚠️ A PATROL TARGET THAT CANNOT BE REACHED IS A PERMANENT STALL. The point is a random spot
        // in a disc around home, so it lands inside a building, on a rooftop or off the navmesh
        // often enough to matter — and the only exit from Patrol used to be arriving at it. With the
        // straight-line fallback gone (see NextPathPoint) the actor would simply stand there for the
        // rest of the session. Give up after PatrolGiveUpSeconds of not arriving and pick another.
        _patrolElapsed += delta;
        if (_patrolElapsed >= PatrolGiveUpSeconds)
        {
            PickPatrolTarget();
        }
    }

    private void TickInvestigate(double delta)
    {
        PlayerCharacter? player = _senses.LivePlayer();
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
            if (_stateTimer >= _profile.InvestigateDuration)
            {
                EnterState(CombatTransition.Resting(_profile.IsAmbusher));
            }
        }
    }

    /// <summary>
    /// Going home (Phase 35D). It ignores the player the whole way — that is the entire point of a
    /// leash, and an "unless it can see you" clause would let the player defeat it by standing in
    /// the way. It re-engages only once it is back well inside its territory
    /// (<see cref="TerritoryLeash.ReturnFraction"/>), so a boundary hover cannot flicker it between
    /// fighting and leaving.
    /// </summary>
    private void TickReturning(double delta)
    {
        float fromHome = HorizontalDistance(_body.GlobalPosition, _home);
        if (!TerritoryLeash.ShouldBreakOff(fromHome, _profile.TerritoryRadius, returning: true))
        {
            // Home ground. Forget the fight entirely: a lingering provoke or a remembered last-known
            // position would put it straight back into combat with whoever it just walked away from.
            _senses.ForgetProvocation();
            _lastKnownPos = _home;
            EnterState(CombatTransition.Resting(_profile.IsAmbusher));
            return;
        }

        FaceTowards(_home);
        MoveTowards(_home, delta, sprint: true, stopDistance: 1f);
    }

    private void TickCombat(double delta)
    {
        PlayerCharacter? player = _senses.LivePlayer();
        Vector3 pos = _lastKnownPos;
        bool canSee = player != null && CanSeePlayer(player, out pos);

        // Should this fight still be happening at all? The five guards and, more importantly, the
        // ORDER of them -- the leash before the health check, so a territorial creature cannot be
        // walked out of its valley one swing at a time -- are in CombatTransition, where they are
        // testable.
        EnemyState next = CombatTransition.Next(
            hasLiveTarget: player != null,
            targetIsHostile: _senses.PlayerIsTarget(_factionId),
            canSeeTarget: canSee,
            distanceFromHome: HorizontalDistance(_body.GlobalPosition, _home),
            territoryRadius: _profile.TerritoryRadius,
            lowHealth: LowHealth(),
            retreatCooldownRemaining: _retreatCooldown);

        if (next != EnemyState.Combat)
        {
            EnterState(next);
            return;
        }

        _lastKnownPos = pos;
        _combatElapsed += delta;

        // A standoff fighter (caster now, archer later) holds a band and kites instead of charging
        // into melee (Phase 29.5F, generalized in 34A). The rule is the *profile*, not the presence
        // of spells: 35C's dragon carries a breath and still closes to bite. Every caster archetype
        // uses ai.caster, whose standoff range already sets IsStandoff, so this is behaviour-neutral
        // for the existing roster.
        if (_profile.IsStandoff)
        {
            TickStandoffCombat(pos, delta);
            return;
        }

        FaceTowards(pos);
        float dist = HorizontalDistance(_body.GlobalPosition, pos);
        if (dist > _profile.AttackRange)
        {
            SetGuard(false);
            // Pack-flankers approach off-axis so a warband surrounds rather than queues (34A).
            MoveTowards(FlankApproach(pos), delta, sprint: true, stopDistance: _profile.AttackRange * 0.85f);
        }
        else
        {
            Stand(delta);

            // Shielded profiles alternate guard-up / swing on a readable rhythm; everyone else
            // just swings (IsUp is always false with no block duration authored).
            bool guard = GuardCycle.IsUp(_combatElapsed, _profile.BlockDuration, _profile.BlockRecovery);
            SetGuard(guard);
            // Range here is horizontal, so a flier hovering directly overhead reads as "in reach"
            // and would swing at empty air the whole time it is up (35B).
            // …and a target on a ledge two metres up is out of reach in exactly the same way, which
            // the Airborne test alone does not cover: it only asks whether THIS actor is flying.
            // Range here is otherwise horizontal, so a flier hovering directly overhead reads as
            // "in reach" and a target on a ledge two metres up reads the same way.
            if (!guard && AiSenseRules.CanSwing(Airborne, pos.Y - _body.GlobalPosition.Y))
            {
                // ⚠️ GATED ON THE ACTION'S OWN RECOVERY, WHICH IS WHAT THIS CALL NEVER HAD.
                // This runs every physics frame, and the only thing that ever rate-limited it was
                // the weapon FSM rejecting a call while committed — so an enemy attacked at the
                // maximum cadence its weapon allowed, forever, with no pause between combos.
                // AiRecoveryRemaining is the authored breath between decisions.
                if (_weapon is { AiRecoveryRemaining: <= 0f })
                {
                    _weapon.TryAttack();
                }
            }
        }
    }

    /// <summary>The point this actor should close on: the target itself for a lone brute, or a spot
    /// swung off the approach line by its pack slot so the group arrives from several sides.</summary>
    private Vector3 FlankApproach(Vector3 targetPos)
    {
        float angle = PackFlank.ApproachAngle(_packSlot, _profile.FlankSpreadDegrees);
        if (angle == 0f)
        {
            return targetPos;
        }

        Vector3 fromTarget = _body.GlobalPosition - targetPos;
        fromTarget.Y = 0f;
        if (fromTarget.LengthSquared() < 0.01f)
        {
            return targetPos;
        }

        // Swing our own bearing around the target, then stand off at weapon reach on that bearing.
        Vector3 bearing = fromTarget.Normalized().Rotated(Vector3.Up, Mathf.DegToRad(angle));
        return targetPos + (bearing * _profile.AttackRange * 0.9f);
    }

    /// <summary>Raises or drops the guard, guarding against writing to a missing combat component.</summary>
    private void SetGuard(bool up)
    {
        if (_combat != null && _combat.IsBlocking != up)
        {
            _combat.IsBlocking = up;
        }
    }

    private void TickRetreat(double delta)
    {
        PlayerCharacter? player = _senses.LivePlayer();
        Vector3 threat = player != null ? player.GlobalPosition : _lastKnownPos;

        Vector3 away = _body.GlobalPosition - threat;
        away.Y = 0f;
        Vector3 fleeTarget = away.LengthSquared() > 0.01f
            ? _body.GlobalPosition + (away.Normalized() * 5f)
            : _home;

        MoveTowards(fleeTarget, delta, sprint: true, stopDistance: 0.1f);
        FaceTowards(threat);

        // A wounded caster heals/wards itself (and lobs spells) as it falls back.
        _tactics.TryCast(_profile, _casting, _combat);

        if (_stateTimer >= _profile.MaxRetreatTime)
        {
            // A coward never rallies; everyone else re-engages once the panic passes.
            EnterState(CombatTransition.AfterRetreat(
                _profile.FleeOnSight, _profile.IsAmbusher, hasLiveTarget: player != null));
        }
    }

    /// <summary>Standoff combat: hold the band, kite when crowded, cast one thing. The whole of it
    /// is in <see cref="EnemyCasterTactics"/>, because it is a self-contained way of fighting that
    /// most archetypes never use.</summary>
    private void TickStandoffCombat(Vector3 targetPos, double delta) =>
        _tactics.TickCombat(_profile, _casting, _combat, targetPos, _home, delta, _frameDelta, Airborne);

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
        if (!_senses.PlayerIsTarget(_factionId))
        {
            return false;
        }

        PlayerCharacter? player = _senses.LivePlayer();
        if (player == null || !CanSeePlayer(player, out Vector3 pos))
        {
            return false;
        }

        // An ambusher sees the target long before it springs: it holds until they walk into the trap.
        if (!AiSenseRules.SpringsAmbush(
                _profile.IsAmbusher, HorizontalDistance(_body.GlobalPosition, pos), _profile.AmbushRange))
        {
            return false;
        }

        _lastKnownPos = pos;

        // A silent profile (AlertRadius 0 — the ambusher's default) doesn't give the pack away.
        if (AiSenseRules.ShoutsOnEngage(_profile.AlertRadius))
        {
            EventBus.Instance?.Publish(
                new EnemyAlertedEvent(Entity!, pos, _profile.AlertRadius, _factionId));
        }

        // A coward's answer to being seen is to run, not to fight.
        EnterState(_profile.FleeOnSight ? EnemyState.Retreat : EnemyState.Combat);
        return true;
    }

    /// <summary>Sight, throttled and cached — see <see cref="EnemySenses"/>. A flier is exempt from
    /// the vertical vision gate in both directions, whether or not it is off the ground right now.</summary>
    private bool CanSeePlayer(PlayerCharacter player, out Vector3 seenPosition) =>
        _senses.CanSeePlayer(_profile, player, canFly: Airborne || _flight != null, out seenPosition);

    /// <summary>Whether this actor currently treats the player as a target — the public read of
    /// <see cref="PlayerIsTarget"/>. A creature that can hold a conversation (35F) is the first thing
    /// that needs to ask this from outside the brain: talking to something mid-swing has to be
    /// refused, and "is it hostile" is a question only the AI can answer (standing *or* provocation).</summary>
    public bool IsHostileToPlayer => _senses.PlayerIsTarget(_factionId);

    private void OnDamaged(DamageDealtEvent e)
    {
        // Being struck by the player is self-defence grounds regardless of standing.
        if (Entity == null || !ReferenceEquals(e.Target, Entity) || e.Source is not PlayerCharacter attacker)
        {
            return;
        }

        _senses.Provoke(_profile.ProvokeMemory);
        if (_state is EnemyState.Idle or EnemyState.Patrol or EnemyState.Investigate)
        {
            _lastKnownPos = attacker.GlobalPosition;
            EnterState(EnemyState.Combat);
        }
    }

    private void OnEnemyAlerted(EnemyAlertedEvent e)
    {
        if (_body == null)
        {
            return;
        }

        // The four filters -- own shout, ambusher, exact faction, personal quarrel -- and why each
        // exists are in AiSenseRules, where they are testable.
        if (!AiSenseRules.AnswersAlert(
                ReferenceEquals(e.Source, Entity), _profile.IsAmbusher, _factionId, e.FactionId, _senses.PlayerIsTarget(_factionId)))
        {
            return;
        }

        if (_state is EnemyState.Idle or EnemyState.Patrol &&
            AiSenseRules.HearsAlert(_body.GlobalPosition, e.Position, e.Radius))
        {
            _lastKnownPos = e.Position;
            EnterState(EnemyState.Investigate);
        }
    }

    // --- Movement helpers ---------------------------------------------------

    /// <summary>Walks toward a point through the navmesh. Arrival is judged against the final
    /// target, never the corner, so the actor does not stop short at a bend.</summary>
    private void MoveTowards(Vector3 target, double delta, bool sprint, float stopDistance) =>
        _nav.MoveTowards(target, delta, sprint, stopDistance, Airborne);

    private void Stand(double delta) => _nav.Stand(delta);

    /// <summary>Turns to face a point — instantly, or slewed at the profile's
    /// <see cref="AIProfileResource.TurnSpeedDegrees"/> for a body too heavy to pivot on the spot.</summary>
    private void FaceTowards(Vector3 target) =>
        _nav.FaceTowards(target, _profile.TurnSpeedDegrees, _frameDelta);

    /// <summary>Signed angle from this actor's facing to a point, in degrees — 0 dead ahead, ±180
    /// directly behind. Drives the directional attack sets (the dragon's bite/wing/tail arcs and the
    /// breath cone), which is why it is public.</summary>
    public float BearingTo(Vector3 target) => _body == null ? 0f : _nav.BearingTo(target);


    // --- Misc helpers -------------------------------------------------------

    private void EnterState(EnemyState next)
    {
        if (_state == next)
        {
            return;
        }

        EnemyState previous = _state;
        _state = next;
        _stateTimer = 0d;

        // The guard only belongs up in melee — leaving combat (or dying) must never strand a corpse
        // or a patrolling enemy in a permanent block.
        // A corpse must also fall rather than hang in the sky, and there is nothing to circle once
        // the fight is over (35B).
        if (next != EnemyState.Combat)
        {
            SetGuard(false);
            _flight?.Ground();
        }

        // Leaving a retreat starts the cooldown, so the fight it re-enters is a fight and not an
        // instant second retreat.
        if (previous == EnemyState.Retreat)
        {
            _retreatCooldown = _profile.RetreatCooldown;
        }

        switch (next)
        {
            case EnemyState.Combat:
                _combatElapsed = 0d;   // every fight starts on the same beat of the guard rhythm
                break;
            case EnemyState.Patrol:
                PickPatrolTarget();
                break;
            case EnemyState.Dead:
                _deathTimer = _profile.DespawnDelay;
                break;
        }

        EventBus.Instance?.Publish(new EnemyStateChangedEvent(Entity!, next));
    }

    private void PickPatrolTarget()
    {
        _patrolElapsed = 0d;
        float angle = GD.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(GD.Randf()) * _profile.PatrolRadius;
        Vector3 candidate = _home + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        _patrolTarget = _nav.SnapToWalkable(candidate);
    }

    private bool LowHealth()
    {
        return _stats != null && _stats.GetNormalized(StatType.Health) < _profile.RetreatHealthFraction;
    }

    /// <summary>True when no player exists or the player is beyond <see cref="ActiveDistance"/>.</summary>
    private bool IsFarFromPlayer()
    {
        PlayerCharacter? player = _senses.AnyPlayer();
        if (player == null)
        {
            return true;
        }

        return _body.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > _profile.ActiveDistance * _profile.ActiveDistance;
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

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.Y = 0f;
        b.Y = 0f;
        return a.DistanceTo(b);
    }
}
