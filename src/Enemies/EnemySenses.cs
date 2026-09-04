using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// What an enemy knows about the world: whether it can see the player, whether it considers them a
/// target, and how long it stays angry about being hit.
///
/// <para>Perception is <b>cached, not computed per frame</b>. The sight check — a field-of-view test
/// and a line-of-sight raycast — runs at most once per <see cref="AIProfileResource.PerceptionInterval"/>
/// and is reused between, so a crowd of enemies does not raycast every physics frame. The ray
/// parameters and its single-element exclude list are built once per actor and only From/To change.</para>
///
/// <para>The rules it applies — the vertical vision gate, the ambush hold, the alert filters — are in
/// <see cref="AiSenseRules"/> where they are engine-free and tested. What is here is the querying:
/// the physics ray, the player lookup, the cache and the provocation clock.</para>
/// </summary>
public sealed class EnemySenses
{
    private readonly IEntity _owner;
    private readonly Node3D _body;

    private PlayerCharacter? _player;
    private PhysicsRayQueryParameters3D? _losQuery;
    private Godot.Collections.Array<Rid>? _losExclude;

    private double _perceptionTimer;
    private bool _cachedCanSee;
    private Vector3 _cachedSeenPos;
    private double _provokeTimer;

    public EnemySenses(IEntity owner, Node3D body)
    {
        _owner = owner;
        _body = body;
    }

    /// <summary>True while this actor is hunting the player because it was struck, regardless of
    /// standing. It never forgets mid-fight; out of combat the memory decays.</summary>
    public bool Provoked { get; private set; }

    /// <summary>Being struck by the player is self-defence grounds regardless of standing.</summary>
    public void Provoke(float memorySeconds)
    {
        Provoked = true;
        _provokeTimer = memorySeconds;
    }

    /// <summary>Forgets the fight entirely. Used when an actor reaches home ground: a lingering
    /// provocation would put it straight back into combat with whoever it just walked away from.</summary>
    public void ForgetProvocation()
    {
        Provoked = false;
        _provokeTimer = 0d;
    }

    /// <summary>
    /// Advances the provocation clock on <b>wall-clock</b> time, so a distant actor ticking at the
    /// far-LOD rate forgets after the same number of real seconds as a near one — see
    /// <see cref="AiLodClock"/> for the six-minute memory that made that necessary.
    /// </summary>
    public void TickProvocation(double wallSeconds, bool inCombat, float provokeMemory)
    {
        if (!Provoked)
        {
            return;
        }

        if (inCombat)
        {
            _provokeTimer = provokeMemory;
            return;
        }

        _provokeTimer -= wallSeconds;
        if (_provokeTimer <= 0d)
        {
            Provoked = false;
        }
    }

    /// <summary>Advances the perception cache's countdown.</summary>
    public void TickPerception(double delta) => _perceptionTimer -= delta;

    /// <summary>
    /// Whether this actor currently treats the player as a target. A faction member engages only
    /// while the player's standing with its faction is hostile (or it has been provoked by a direct
    /// attack); an unfactioned actor is hostile by default.
    /// </summary>
    public bool PlayerIsTarget(string factionId)
    {
        if (Provoked || string.IsNullOrEmpty(factionId))
        {
            return true;
        }

        ReputationComponent? reputation = Player()?.GetComponent<ReputationComponent>();
        return reputation == null || reputation.IsHostile(factionId);
    }

    /// <summary>The player if there is one, it is still in the tree, and it is alive — else null.
    /// A dead player is not a target, which is what ends a fight when they fall.</summary>
    public PlayerCharacter? LivePlayer()
    {
        if (Player() is not { } player || !GodotObject.IsInstanceValid(player))
        {
            return null;
        }

        Stats.StatsComponent? stats = player.GetComponent<Stats.StatsComponent>();
        return stats == null || stats.IsAlive ? player : null;
    }

    /// <summary>The player whether or not they are alive, for the range checks that only care where
    /// they are — the level-of-detail test does not stop applying because they died.</summary>
    public PlayerCharacter? AnyPlayer() => Player();

    /// <summary>Sight, throttled: the costly check runs at most once per perception interval and is
    /// cached between.</summary>
    public bool CanSeePlayer(AIProfileResource profile, PlayerCharacter player, bool canFly, out Vector3 seenPosition)
    {
        if (_perceptionTimer <= 0d)
        {
            _cachedCanSee = Compute(profile, player, canFly, out _cachedSeenPos);
            _perceptionTimer = profile.PerceptionInterval;
        }

        seenPosition = _cachedSeenPos;
        return _cachedCanSee;
    }

    public void Dispose()
    {
        _losQuery?.Dispose();
        _losQuery = null;
        _losExclude = null;
    }

    private bool Compute(AIProfileResource profile, PlayerCharacter player, bool canFly, out Vector3 seenPosition)
    {
        Vector3 selfPos = _body.GlobalPosition;
        Vector3 playerPos = player.GlobalPosition;
        seenPosition = playerPos;

        Vector3 flat = playerPos - selfPos;
        flat.Y = 0f;
        float distance = flat.Length();
        if (distance > profile.VisionRange || distance < 0.001f)
        {
            return distance <= profile.VisionRange; // standing on the player still counts as seen
        }

        if (!AiSenseRules.PassesVerticalVisionGate(playerPos.Y - selfPos.Y, canFly))
        {
            return false;
        }

        // Outside the proximity bubble the target must be within the view cone.
        if (distance > profile.ProximityRange)
        {
            Vector3 forward = -_body.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            if (!EnemyPerception.InViewCone(forward, flat, profile.FovDegrees))
            {
                return false;
            }
        }

        return HasLineOfSight(player, selfPos + (Vector3.Up * 1.6f), playerPos + (Vector3.Up * 1.2f));
    }

    private bool HasLineOfSight(PlayerCharacter player, Vector3 from, Vector3 to)
    {
        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;

        // Built once; the excluded RID (this body) never changes and only From/To vary per cast.
        if (_losQuery == null)
        {
            _losExclude = new Godot.Collections.Array<Rid> { ((CollisionObject3D)_body).GetRid() };
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
        return hit["collider"].AsGodotObject() is not Node node || ReferenceEquals(EntityNode.FindOwner(node), player);
    }

    private PlayerCharacter? Player()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            return _player;
        }

        _player = null;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter found))
        {
            _player = found;
        }

        return _player;
    }
}
