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

    /// <summary>Band a caster falls back to when its profile authors no standoff range — the
    /// pre-34A <c>CastRange</c> default, so an unconverted caster fights exactly as it used to.</summary>
    private const float DefaultCastRange = 14f;

    /// <summary>Which <see cref="AIProfileResource"/> drives this actor (see <c>data/ai_profiles/</c>).
    /// An unknown id falls back to a default brute profile with a warning, so a content typo degrades
    /// to "fights plainly" rather than a dead brain.</summary>
    [Export] public string ProfileId { get; set; } = DefaultProfileId;

    /// <summary>Directly-assigned profile, for tests and for scenes that would rather inline it than
    /// go through the database. Wins over <see cref="ProfileId"/> when set.</summary>
    [Export] public AIProfileResource? Profile { get; set; }

    private AIProfileResource _profile = AIProfileResource.CreateDefault();

    /// <summary>The resolved profile driving this actor.</summary>
    public AIProfileResource ActiveProfile => _profile;

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

    // Guard rhythm (shielded profiles) + this actor's slot in the pack fan-out.
    private double _combatElapsed;
    private int _packSlot;

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

        _profile = ResolveProfile();

        // A stable per-instance slot, so members of a pack fan to different sides of the target and
        // keep that side for their whole life rather than jittering between approaches each tick.
        _packSlot = (int)(body.GetInstanceId() % 5UL);

        _body = body;
        _stats = Entity.GetComponent<StatsComponent>();
        _weapon = Entity.GetComponent<MeleeWeaponComponent>();
        _casting = Entity.GetComponent<SpellcastingComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _flight = Entity.GetComponent<FlightComponent>();
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

    /// <summary>An inline <see cref="Profile"/> wins; otherwise the id is looked up in the database.
    /// A miss warns once and falls back to a brute so the actor still fights.</summary>
    private AIProfileResource ResolveProfile()
    {
        if (Profile != null)
        {
            return Profile;
        }

        if (AIProfileDatabase.Get(ProfileId) is { } found)
        {
            return found;
        }

        Log.Warn($"AI profile '{ProfileId}' is not registered; falling back to '{DefaultProfileId}'.");
        return AIProfileResource.CreateDefault();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        _perceptionTimer -= delta;
        // Cached so FaceTowards can slew at a profile's turn rate without threading delta through
        // the four state ticks that call it (35A).
        _frameDelta = delta;

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

            _sleepTimer = _profile.SleepInterval;
        }

        _stateTimer += delta;

        // Provoke memory: a struck enemy hunts the player, but forgets after a calm spell so it stands
        // down once reputation is no longer hostile (it never forgets mid-fight).
        if (_provoked)
        {
            if (_state == EnemyState.Combat)
            {
                _provokeTimer = _profile.ProvokeMemory;
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
            if (_stateTimer >= _profile.InvestigateDuration)
            {
                EnterState(_profile.IsAmbusher ? EnemyState.Idle : EnemyState.Patrol);
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
            _provoked = false;
            _lastKnownPos = _home;
            EnterState(_profile.IsAmbusher ? EnemyState.Idle : EnemyState.Patrol);
            return;
        }

        FaceTowards(_home);
        MoveTowards(_home, delta, sprint: true, stopDistance: 1f);
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

        // Drawn too far from its ground: break off (35D). Checked before anything else in the fight
        // so a territorial creature cannot be walked out of its valley one swing at a time.
        if (TerritoryLeash.ShouldBreakOff(
                HorizontalDistance(_body.GlobalPosition, _home), _profile.TerritoryRadius, returning: false))
        {
            EnterState(EnemyState.Returning);
            return;
        }

        _lastKnownPos = pos;
        _combatElapsed += delta;

        if (LowHealth())
        {
            EnterState(EnemyState.Retreat);
            return;
        }

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
            if (!guard && !Airborne)
            {
                _weapon?.TryAttack();
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

        if (_stateTimer >= _profile.MaxRetreatTime)
        {
            // A coward never rallies: it goes back to its business and flees again on the next
            // sighting. Everyone else re-engages once the panic passes.
            if (_profile.FleeOnSight)
            {
                EnterState(_profile.IsAmbusher ? EnemyState.Idle : EnemyState.Patrol);
                return;
            }

            EnterState(player != null ? EnemyState.Combat : EnemyState.Investigate);
        }
    }

    // --- Standoff behaviour (Phase 29.5F, generalized 34A) ------------------

    /// <summary>Standoff combat: hold the band (approach when too far, kite when too close), face the
    /// target so the attack aims true, and fire whatever's ready. Reuses the player's
    /// <see cref="SpellcastingComponent"/> — no parallel casting system.</summary>
    private void TickStandoffCombat(Vector3 targetPos, double delta)
    {
        SetGuard(false);
        FaceTowards(targetPos);
        float dist = HorizontalDistance(_body.GlobalPosition, targetPos);
        float band = _profile.StandoffRange > 0f ? _profile.StandoffRange : DefaultCastRange;
        switch (CasterDecision.Move(dist, _profile.KiteDistance, band))
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
                MoveTowards(targetPos, delta, sprint: false, stopDistance: band * 0.9f);
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
        float lowest = _profile.AllyHealThreshold;

        foreach (Node node in GetTree().GetNodesInGroup(Quests.ObjectiveLocator.EnemyGroup))
        {
            if (node is not Node3D body ||
                HorizontalDistance(_body.GlobalPosition, body.GlobalPosition) > _profile.AllySupportRange ||
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
        if (player == null || !CanSeePlayer(player, out Vector3 pos))
        {
            return false;
        }

        // An ambusher sees the target long before it springs: it holds until they walk into the trap.
        if (_profile.IsAmbusher &&
            HorizontalDistance(_body.GlobalPosition, pos) > _profile.AmbushRange)
        {
            return false;
        }

        _lastKnownPos = pos;

        // A silent profile (AlertRadius 0 — the ambusher's default) doesn't give the pack away.
        if (_profile.AlertRadius > 0f)
        {
            EventBus.Instance?.Publish(new EnemyAlertedEvent(Entity!, pos));
        }

        // A coward's answer to being seen is to run, not to fight.
        EnterState(_profile.FleeOnSight ? EnemyState.Retreat : EnemyState.Combat);
        return true;
    }

    /// <summary>Perception, throttled: the (relatively costly) sight check — FOV + line-of-sight
    /// raycast — runs at most once per <see cref="PerceptionInterval"/> and is cached between,
    /// so a crowd of enemies doesn't raycast every physics frame.</summary>
    private bool CanSeePlayer(PlayerCharacter player, out Vector3 seenPosition)
    {
        if (_perceptionTimer <= 0d)
        {
            _cachedCanSee = ComputeCanSeePlayer(player, out _cachedSeenPos);
            _perceptionTimer = _profile.PerceptionInterval;
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
        if (dist > _profile.VisionRange || dist < 0.001f)
        {
            return dist <= _profile.VisionRange; // standing on the player still counts as seen
        }

        // Outside the proximity bubble the target must be within the view cone.
        if (dist > _profile.ProximityRange)
        {
            Vector3 forward = -_body.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (!EnemyPerception.InViewCone(forward, flat, _profile.FovDegrees))
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
    /// <summary>Whether this actor currently treats the player as a target — the public read of
    /// <see cref="PlayerIsTarget"/>. A creature that can hold a conversation (35F) is the first thing
    /// that needs to ask this from outside the brain: talking to something mid-swing has to be
    /// refused, and "is it hostile" is a question only the AI can answer (standing *or* provocation).</summary>
    public bool IsHostileToPlayer => PlayerIsTarget();

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
        _provokeTimer = _profile.ProvokeMemory;
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

        // An ambusher holds its trap even when the pack starts shouting — walking to the noise is
        // exactly what would give the ambush away.
        if (_profile.IsAmbusher)
        {
            return;
        }

        if (_state is EnemyState.Idle or EnemyState.Patrol &&
            HorizontalDistance(_body.GlobalPosition, e.Position) <= _profile.AlertRadius)
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
        // Airborne, the navmesh is the wrong map: its corners route around ground obstacles this
        // actor is currently flying over. Steer straight (35B).
        if (_agent == null || Airborne)
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

    /// <summary>Turns to face a point — instantly, or slewed at the profile's
    /// <see cref="AIProfileResource.TurnSpeedDegrees"/> for a body too heavy to pivot on the spot.</summary>
    private void FaceTowards(Vector3 target)
    {
        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) <= 0.0004f)
        {
            return;
        }

        if (_profile.TurnSpeedDegrees <= 0f)
        {
            _body.LookAt(flat, Vector3.Up);
            return;
        }

        float desired = Mathf.Atan2(pos.X - flat.X, pos.Z - flat.Z);
        float step = Mathf.DegToRad(_profile.TurnSpeedDegrees) * (float)_frameDelta;
        _body.Rotation = _body.Rotation with { Y = Mathf.RotateToward(_body.Rotation.Y, desired, step) };
    }

    /// <summary>Signed angle from this actor's facing to a point, in degrees — 0 dead ahead, ±180
    /// directly behind. Drives the directional attack set (35A).</summary>
    public float BearingTo(Vector3 target)
    {
        if (_body == null)
        {
            return 0f;
        }

        Vector3 pos = _body.GlobalPosition;
        var flat = new Vector3(target.X, pos.Y, target.Z);
        if (flat.DistanceSquaredTo(pos) <= 0.0004f)
        {
            return 0f;
        }

        float desired = Mathf.Atan2(pos.X - flat.X, pos.Z - flat.Z);
        return Mathf.RadToDeg(Mathf.AngleDifference(_body.Rotation.Y, desired));
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

        // The guard only belongs up in melee — leaving combat (or dying) must never strand a corpse
        // or a patrolling enemy in a permanent block.
        // A corpse must also fall rather than hang in the sky, and there is nothing to circle once
        // the fight is over (35B).
        if (next != EnemyState.Combat)
        {
            SetGuard(false);
            _flight?.Ground();
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
        float angle = GD.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(GD.Randf()) * _profile.PatrolRadius;
        _patrolTarget = _home + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private bool LowHealth()
    {
        return _stats != null && _stats.GetNormalized(StatType.Health) < _profile.RetreatHealthFraction;
    }

    /// <summary>True when no player exists or the player is beyond <see cref="ActiveDistance"/>.</summary>
    private bool IsFarFromPlayer()
    {
        PlayerCharacter? player = GetPlayer();
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
