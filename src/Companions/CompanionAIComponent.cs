using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// The decision-making brain for a recruited <see cref="CompanionEntity"/> (Phase 32A) — the ally
/// mirror of <see cref="Enemies.EnemyAIComponent"/>. It drives the <em>same</em> shared parts the
/// player and the enemies use: <see cref="LocomotionComponent"/> to move (via the same
/// <see cref="PathSteering"/> navmesh rule), <see cref="MeleeWeaponComponent"/> to attack, and
/// <see cref="CombatComponent"/>'s team to decide who counts as a hostile. There is no parallel
/// follower movement or combat system.
///
/// The loop is anchor-driven: each tick it works out where it belongs (its
/// <see cref="CompanionFormation"/> slot behind the player under
/// <see cref="CompanionStance.Follow"/>, or the spot it was told to hold), picks the nearest hostile
/// worth fighting, and hands both distances to the pure <see cref="CompanionDecision"/>. The leash
/// keeps it from being kited away: dragged too far from its anchor it breaks off and regroups.
///
/// A companion that runs out of health is <em>downed</em>, never lost — it drops out of the fight and
/// stands back up on a timer with a fraction of its health.
///
/// It persists the state the roster cannot see (Phase 32D): the hold anchor — a Hold order means
/// nothing if the spot is forgotten on load — and the downed/recovery countdown, so quitting while a
/// companion is on the ground doesn't quietly reset their fight.
/// </summary>
[GlobalClass]
public partial class CompanionAIComponent : EntityComponent, ISaveable
{
    [ExportGroup("Formation")]
    /// <summary>Metres behind the player the formation slot sits.</summary>
    [Export] public float FollowDistance { get; set; } = 3.0f;

    /// <summary>Arrival deadzone around the anchor, so a companion in position doesn't shuffle.</summary>
    [Export] public float SlotTolerance { get; set; } = 1.4f;

    /// <summary>Distance from the anchor past which it sprints to catch up.</summary>
    [Export] public float SprintDistance { get; set; } = 7f;

    /// <summary>The companion's slot in the party fan (assigned by the roster).</summary>
    [Export] public int SlotIndex { get; set; }

    [ExportGroup("Combat")]
    /// <summary>Radius around the companion scanned for hostiles to engage.</summary>
    [Export] public float EngageRadius { get; set; } = 14f;

    /// <summary>Weapon reach — inside this it swings instead of closing.</summary>
    [Export] public float AttackRange { get; set; } = 2.1f;

    /// <summary>How far it may stray from its anchor before it breaks off a fight and regroups.</summary>
    [Export] public float LeashRadius { get; set; } = 18f;

    /// <summary>Seconds between hostile scans; the chosen target is cached in between.</summary>
    [Export] public float ScanInterval { get; set; } = 0.3f;

    /// <summary>Seconds with nothing to fight before an engage order is considered spent.</summary>
    [Export] public float EngageStandDownSeconds { get; set; } = 4f;

    [ExportGroup("Downed")]
    /// <summary>Seconds a downed companion stays out before standing back up.</summary>
    [Export] public float RecoverySeconds { get; set; } = 12f;

    /// <summary>Fraction of max health it recovers with.</summary>
    [Export] public float RecoveryHealthFraction { get; set; } = 0.4f;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private MeleeWeaponComponent? _weapon;
    private CombatComponent? _combat;
    private NavigationAgent3D? _agent;
    private PlayerCharacter? _player;

    private CompanionState _state = CompanionState.Idle;
    private CompanionStance _stance = CompanionStance.Follow;
    private Vector3 _holdAnchor;
    private IEntity? _target;
    private double _scanTimer;
    private double _recoveryTimer;
    private double _idleSinceOrder;

    public CompanionState State => _state;

    public CompanionStance Stance => _stance;

    /// <summary>Whether an <see cref="CompanionStance.Engage"/> order has run its course — nothing
    /// hostile left within reach for <see cref="EngageStandDownSeconds"/>. The roster polls this and
    /// returns the companion to <see cref="CompanionStance.Follow"/>.</summary>
    public bool EngageOrderSpent =>
        _stance == CompanionStance.Engage && _idleSinceOrder >= EngageStandDownSeconds;

    /// <summary>The companion id of the owning actor (empty when the owner isn't a companion).</summary>
    public string CompanionId => (Entity as CompanionEntity)?.CompanionId ?? string.Empty;

    /// <summary>The owner's name <c>Loc</c> key, for the events this brain publishes.</summary>
    private string NameKey => (Entity as CompanionEntity)?.NameKey ?? string.Empty;

    /// <summary>
    /// Sets the standing order. Switching to <see cref="CompanionStance.Hold"/> anchors the companion
    /// to <paramref name="holdAnchor"/> (its current position when null) — that spot becomes what the
    /// leash and the formation logic measure against until it is told to follow again.
    /// </summary>
    public void SetStance(CompanionStance stance, Vector3? holdAnchor = null)
    {
        _stance = stance;
        _idleSinceOrder = 0d;
        if (stance == CompanionStance.Hold)
        {
            _holdAnchor = holdAnchor ?? (_body != null ? _body.GlobalPosition : Vector3.Zero);
        }
    }

    protected override void OnInitialize()
    {
        if (Entity!.Body is not CharacterBody3D body)
        {
            Log.Error($"{nameof(CompanionAIComponent)} requires a CharacterEntity owner.");
            return;
        }

        _body = body;
        _stats = Entity.GetComponent<StatsComponent>();
        _weapon = Entity.GetComponent<MeleeWeaponComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
        _agent = _body.GetNodeOrNull<NavigationAgent3D>("NavAgent");
        _holdAnchor = _body.GlobalPosition;

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamageDealt);
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
        SaveManager.Instance?.Unregister(this);
    }

    // --- Persistence (32D) ---------------------------------------------------

    public string SaveId => SaveKey("companion_ai");

    public Godot.Collections.Dictionary Save() => new()
    {
        ["hold_x"] = _holdAnchor.X,
        ["hold_y"] = _holdAnchor.Y,
        ["hold_z"] = _holdAnchor.Z,
        ["downed"] = _state == CompanionState.Downed,
        ["recovery"] = _recoveryTimer,
    };

    public void Load(Godot.Collections.Dictionary data)
    {
        _holdAnchor = new Vector3(
            data.TryGetValue("hold_x", out Variant hx) ? hx.AsSingle() : _holdAnchor.X,
            data.TryGetValue("hold_y", out Variant hy) ? hy.AsSingle() : _holdAnchor.Y,
            data.TryGetValue("hold_z", out Variant hz) ? hz.AsSingle() : _holdAnchor.Z);

        if (data.TryGetValue("downed", out Variant downed) && downed.AsBool())
        {
            // Resume the countdown where it stopped, but never below a beat of it — a companion that
            // loads in on the ground should visibly get back up, not pop upright on the first frame.
            _recoveryTimer = System.Math.Max(
                data.TryGetValue("recovery", out Variant left) ? left.AsDouble() : RecoverySeconds,
                1d);
            _target = null;
            _state = CompanionState.Downed;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        if (_state == CompanionState.Downed)
        {
            TickDowned(delta);
            return;
        }

        if (_stats is { IsAlive: false })
        {
            EnterDowned();
            return;
        }

        _scanTimer -= delta;
        if (_scanTimer <= 0d)
        {
            _scanTimer = ScanInterval;
            _target = PickTarget();
        }
        else if (!IsLiveHostile(_target))
        {
            _target = null;
        }

        // The standing order stretches the engagement envelope (32B) — the decision rule itself is
        // the same for every order, only its distances differ.
        float leash = CompanionOrders.Leash(_stance, LeashRadius);

        Vector3 anchor = CurrentAnchor();
        float anchorDistance = HorizontalDistance(_body.GlobalPosition, anchor);
        bool hasTarget = _target != null;
        Vector3 targetPos = hasTarget ? _target!.Body.GlobalPosition : Vector3.Zero;
        float targetDistance = hasTarget ? HorizontalDistance(_body.GlobalPosition, targetPos) : 0f;

        // An engage order is finished when there is nothing left to fight; the roster then returns
        // the companion to formation, so "sic 'em" doesn't strand it in attack posture forever.
        _idleSinceOrder = hasTarget ? 0d : _idleSinceOrder + delta;

        switch (CompanionDecision.Decide(
            anchorDistance, hasTarget, targetDistance, leash, SlotTolerance, AttackRange))
        {
            case CompanionAction.Attack:
                EnterState(CompanionState.Combat);
                FaceTowards(targetPos);
                Stand(delta);
                _weapon?.TryAttack();
                break;

            case CompanionAction.Chase:
                EnterState(CompanionState.Combat);
                FaceTowards(targetPos);
                MoveTowards(targetPos, delta, sprint: targetDistance > SprintDistance, stopDistance: AttackRange * 0.85f);
                break;

            case CompanionAction.Regroup:
                // Out past the leash is a break-off (the target is dropped so it doesn't immediately
                // re-engage on arrival); merely out of formation is an ordinary catch-up.
                bool leashed = anchorDistance > leash;
                if (leashed)
                {
                    _target = null;
                }

                EnterState(leashed ? CompanionState.Regroup : CompanionState.Follow);
                MoveTowards(anchor, delta, sprint: anchorDistance > SprintDistance, stopDistance: SlotTolerance);
                break;

            default:
                EnterState(CompanionState.Idle);
                Stand(delta);
                FaceLikePlayer();
                break;
        }
    }

    // --- Downed / recovery ---------------------------------------------------

    private void TickDowned(double delta)
    {
        Stand(delta);
        _recoveryTimer -= delta;
        if (_recoveryTimer > 0d)
        {
            return;
        }

        // Stand back up with a fraction of max health. Companions are story actors — losing one to a
        // stray goblin would silently break the quests hung off them.
        if (_stats != null)
        {
            _stats.SetCurrent(StatType.Health, _stats.GetMax(StatType.Health) * RecoveryHealthFraction);
        }

        _target = null;
        EnterState(CompanionState.Idle);
        EventBus.Instance?.Publish(new CompanionDownedEvent(CompanionId, NameKey, false));
    }

    private void EnterDowned()
    {
        _recoveryTimer = RecoverySeconds;
        _target = null;
        if (_combat != null)
        {
            _combat.IsBlocking = false;
        }

        EnterState(CompanionState.Downed);
        EventBus.Instance?.Publish(new CompanionDownedEvent(CompanionId, NameKey, true));
    }

    // --- Targeting -----------------------------------------------------------

    /// <summary>
    /// The hostile to fight this scan. <b>Assist focus first (32B):</b> whatever the player is locked
    /// onto wins outright — a companion that ignores the thing you are visibly fighting reads as
    /// broken — and only when the player is not locked on does it fall back to the nearest hostile
    /// inside its order's scan radius.
    ///
    /// Candidates come from the shared targetable-enemy group every hostile joins on spawn, filtered
    /// by team, so the companion picks fights by the same friendly-fire rule the hitboxes enforce and
    /// never targets the player or another companion.
    /// </summary>
    private IEntity? PickTarget()
    {
        int team = _combat?.Team ?? 0;
        float radius = CompanionOrders.EngageRadius(_stance, EngageRadius);

        if (PlayerFocus() is { } focus && IsHostileTo(focus, team) &&
            HorizontalDistance(_body.GlobalPosition, focus.Body.GlobalPosition) <= radius)
        {
            return focus;
        }

        IEntity? best = null;
        float bestDistance = radius;

        foreach (Node node in GetTree().GetNodesInGroup(ObjectiveLocator.EnemyGroup))
        {
            if (node is not Node3D candidateBody || !IsInstanceValid(candidateBody) ||
                EntityNode.FindOwner(node) is not { } candidate || !IsHostileTo(candidate, team))
            {
                continue;
            }

            float distance = HorizontalDistance(_body.GlobalPosition, candidateBody.GlobalPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The entity the player is locked onto, if any — the companion's assist focus.</summary>
    private IEntity? PlayerFocus() => GetPlayer()?.GetComponent<LockOnComponent>()?.Target;

    /// <summary>Whether <paramref name="candidate"/> is a live actor on an opposing team.</summary>
    private static bool IsHostileTo(IEntity? candidate, int team) =>
        candidate != null && IsInstanceValid(candidate.Body) &&
        candidate.GetComponent<CombatComponent>() is { } combat && combat.Team != team &&
        candidate.GetComponent<StatsComponent>() is { IsAlive: true };

    private bool IsLiveHostile(IEntity? entity)
    {
        return entity != null && IsInstanceValid(entity.Body) &&
            entity.GetComponent<StatsComponent>() is { IsAlive: true };
    }

    /// <summary>
    /// Defends on reaction: a blow landed on the player (or on this companion) makes the attacker the
    /// target immediately, without waiting for the next scan. This is what stops a companion trailing
    /// obliviously while the player is being hit from behind.
    /// </summary>
    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_state == CompanionState.Downed || e.Source == null || Entity == null ||
            ReferenceEquals(e.Source, Entity))
        {
            return;
        }

        bool defendsTarget = ReferenceEquals(e.Target, Entity) || e.Target is PlayerCharacter;
        if (!defendsTarget || !IsLiveHostile(e.Source) ||
            e.Source.GetComponent<CombatComponent>()?.Team == (_combat?.Team ?? 0))
        {
            return;
        }

        if (HorizontalDistance(_body.GlobalPosition, e.Source.Body.GlobalPosition) <= EngageRadius)
        {
            _target = e.Source;
        }
    }

    // --- Anchor & movement ---------------------------------------------------

    /// <summary>Where the companion belongs right now: its formation slot behind the player while
    /// following, or the anchor it was told to hold.</summary>
    private Vector3 CurrentAnchor()
    {
        if (_stance == CompanionStance.Hold)
        {
            return _holdAnchor;
        }

        PlayerCharacter? player = GetPlayer();
        if (player == null)
        {
            return _body.GlobalPosition;
        }

        return CompanionFormation.Slot(
            player.GlobalPosition, -player.GlobalTransform.Basis.Z, SlotIndex, FollowDistance);
    }

    private void MoveTowards(Vector3 target, double delta, bool sprint, float stopDistance)
    {
        // Same navmesh steering rule the enemies use (Phase 27A): steer at the next path corner when a
        // baked navmesh is under the agent, but judge arrival against the final target.
        Vector3 corner = NextPathPoint(target);
        Vector3 toCorner = corner - _body.GlobalPosition;
        toCorner.Y = 0f;
        float cornerDistance = toCorner.Length();
        float finalDistance = HorizontalDistance(_body.GlobalPosition, target);
        Vector3 wish = PathSteering.ShouldSteer(cornerDistance, finalDistance, stopDistance)
            ? toCorner.Normalized()
            : Vector3.Zero;

        if (wish != Vector3.Zero && _state != CompanionState.Combat)
        {
            FaceTowards(_body.GlobalPosition + wish);
        }

        GetLocomotion()?.Move(delta, wish, sprint, jump: false);
    }

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

    /// <summary>Idling in formation, a companion looks the way the player does — standing nose-to-nose
    /// with whoever it last walked toward reads as broken.</summary>
    private void FaceLikePlayer()
    {
        if (_stance != CompanionStance.Follow || GetPlayer() is not { } player)
        {
            return;
        }

        FaceTowards(_body.GlobalPosition + (-player.GlobalTransform.Basis.Z));
    }

    private LocomotionComponent? GetLocomotion() => Entity?.GetComponent<LocomotionComponent>();

    // --- Misc helpers --------------------------------------------------------

    private void EnterState(CompanionState next)
    {
        if (_state == next)
        {
            return;
        }

        _state = next;
        EventBus.Instance?.Publish(new CompanionStateChangedEvent(Entity!, next));
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
