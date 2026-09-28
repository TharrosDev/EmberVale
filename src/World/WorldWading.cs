using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Stats;
using Godot;

namespace Embervale.World;

/// <summary>
/// Applies <see cref="WadingRules"/> to the bodies in the world: water slows whoever stands in it,
/// warns the player before it gets too deep, and past <see cref="WorldWater.WadeDepth"/> leans them
/// back towards the shallows.
///
/// ⚠️ <b>SPEED GOES THROUGH THE STAT, NOT THE MOTOR.</b> Wading is a <see cref="StatModifier"/>
/// (PercentMult, sourced to this node) on <see cref="StatType.MoveSpeed"/>, exactly like a slowing
/// buff. That is what makes it apply to every body the same way — player, enemies, townsfolk,
/// companions — and to walk and sprint alike, because <see cref="LocomotionComponent"/> reads the
/// stat for both. It also means nothing persists: the modifier is not saved, and a body leaving the
/// water or the world has it stripped by source.
///
/// <b>Player every physics frame; everyone else every <see cref="ActorRefreshSeconds"/>.</b> The
/// player feels the pace change underfoot; an enemy wading after them can afford a quarter-second
/// lag, and a sweep of the enemy and NPC groups on every frame would be the only per-frame group
/// walk in the world layer.
///
/// <b>Push-back is player-only.</b> An AI body is steered by navigation that already treats deep
/// water as impassable; nudging it sideways would fight the navigator for no gain.
///
/// Created by <see cref="WorldRecovery"/> as its child, because they are two halves of one contract:
/// this keeps the player out of deep water, recovery gets them out when something else put them in.
/// </summary>
public sealed partial class WorldWading : Node
{
    // --- Knobs (defaults are WadingTuning.Default; see WadingRules for what each band means) ---

    [Export(PropertyHint.Range, "0,1,0.01")] public float AnkleDepth { get; set; } = WadingTuning.Default.AnkleDepth;
    [Export(PropertyHint.Range, "0,1.1,0.01")] public float KneeDepth { get; set; } = WadingTuning.Default.KneeDepth;
    [Export(PropertyHint.Range, "0,1.1,0.01")] public float WaistDepth { get; set; } = WadingTuning.Default.WaistDepth;
    [Export(PropertyHint.Range, "0,1.1,0.01")] public float WarnDepth { get; set; } = WadingTuning.Default.WarnDepth;
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float KneeSpeed { get; set; } = WadingTuning.Default.KneeSpeed;
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float WaistSpeed { get; set; } = WadingTuning.Default.WaistSpeed;
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float LimitSpeed { get; set; } = WadingTuning.Default.LimitSpeed;
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float DeepSpeed { get; set; } = WadingTuning.Default.DeepSpeed;
    [Export(PropertyHint.Range, "0,8,0.1")] public float PushBackSpeed { get; set; } = WadingTuning.Default.PushBackSpeed;

    /// <summary>Seconds between speed refreshes for non-player bodies.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float ActorRefreshSeconds { get; set; } = 0.25f;

    /// <summary>Minimum seconds between two "too deep" warnings, so pacing the shelf edge does not
    /// stack a column of identical toasts.</summary>
    [Export(PropertyHint.Range, "0,60,0.5")] public float WarningCooldownSeconds { get; set; } = 8f;

    /// <summary>Minimum seconds between two wading splash cues.</summary>
    [Export(PropertyHint.Range, "0,5,0.05")] public float SplashCooldownSeconds { get; set; } = 0.6f;

    /// <summary>Sound cue played when the player wades in past the knee.</summary>
    public const string SplashCue = "sfx.water.wade";

    /// <summary>Locale key of the "too deep" warning.</summary>
    public const string TooDeepKey = "water.too_deep";

    private sealed class Applied
    {
        public required StatsComponent Stats { get; init; }
        public StatModifier? Modifier { get; set; }
        public float Scale { get; set; } = 1f;
        public bool Seen { get; set; }
    }

    private readonly Dictionary<ulong, Applied> _applied = new();
    private WadingTuning _tuning = WadingTuning.Default;
    private float _actorTimer;
    private float _warningCooldown;
    private float _splashCooldown;
    private ulong _playerId;
    private StatsComponent? _playerStats;
    private LocomotionComponent? _playerLocomotion;
    private float _playerDepth;
    private WadingBand _playerBand = WadingBand.Dry;

    /// <summary>The player's wading depth as of the last physics frame. Diagnostics and HUD only.</summary>
    public float PlayerDepth => _playerDepth;

    /// <summary>The player's wading band as of the last physics frame.</summary>
    public WadingBand PlayerBand => _playerBand;

    public override void _Ready()
    {
        _tuning = new WadingTuning
        {
            AnkleDepth = AnkleDepth,
            KneeDepth = KneeDepth,
            WaistDepth = WaistDepth,
            WarnDepth = WarnDepth,
            KneeSpeed = KneeSpeed,
            WaistSpeed = WaistSpeed,
            LimitSpeed = LimitSpeed,
            DeepSpeed = DeepSpeed,
            PushBackSpeed = PushBackSpeed,
        };
    }

    public override void _ExitTree()
    {
        foreach (Applied applied in _applied.Values)
        {
            Strip(applied);
        }
        _applied.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _warningCooldown -= dt;
        _splashCooldown -= dt;

        if (WorldGround.Field is not { } field || GameManager.Instance is not { IsPlaying: true })
        {
            return;
        }

        if (ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out PlayerCharacter player) &&
            IsInstanceValid(player))
        {
            TickPlayer(player, field, dt);
        }

        _actorTimer += dt;
        if (_actorTimer >= ActorRefreshSeconds)
        {
            _actorTimer = 0f;
            TickActors(field);
        }
    }

    private void TickPlayer(PlayerCharacter player, WorldHeightfield field, float dt)
    {
        // Components are looked up once per player instance, not per frame: a save/load rebuild makes
        // a new player, and that is the only time these change.
        if (_playerId != player.GetInstanceId() || _playerStats == null)
        {
            _playerId = player.GetInstanceId();
            _playerStats = player.GetComponent<StatsComponent>();
            _playerLocomotion = player.GetComponent<LocomotionComponent>();
        }

        Vector3 feet = player.GlobalPosition;
        float depth = WorldWater.WadingDepthAt(feet.X, feet.Y, feet.Z, field);
        WadingBand band = WadingRules.BandFor(depth, _tuning);
        bool flying = _playerLocomotion is { } locomotion && IsInstanceValid(locomotion) && locomotion.Flying;

        if (_playerStats is { } stats && IsInstanceValid(stats))
        {
            Apply(player, stats, flying ? 1f : WadingRules.QuantizedSpeedScale(depth, _tuning), seen: false);
        }

        if (WadingRules.CrossedWarning(_playerDepth, depth, _tuning) && _warningCooldown <= 0f)
        {
            _warningCooldown = WarningCooldownSeconds;
            EventBus.Instance?.Publish(new WorldHazardNoticeEvent(TooDeepKey));
        }

        if (band >= WadingBand.Knee && _playerBand < WadingBand.Knee && _splashCooldown <= 0f)
        {
            _splashCooldown = SplashCooldownSeconds;
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(SplashCue, feet));
        }

        _playerDepth = depth;
        _playerBand = band;

        float push = WadingRules.PushBack(depth, _tuning);
        if (push <= 0f || flying || UiState.MenuOpen)
        {
            return;
        }

        (float x, float z) = WadingRules.ShallowerDirection(
            (sx, sz) => WorldWater.DepthAt(sx, sz, field), feet.X, feet.Z);
        if (x == 0f && z == 0f)
        {
            // A flat-bottomed basin has no downhill to the shore. The last safe ground does.
            if (GetParent() is WorldRecovery { LastSafeGround: { } safe })
            {
                Vector3 toSafe = new Vector3(safe.X - feet.X, 0f, safe.Z - feet.Z);
                if (toSafe.LengthSquared() > 0.01f)
                {
                    toSafe = toSafe.Normalized();
                    x = toSafe.X;
                    z = toSafe.Z;
                }
            }
        }
        if (x == 0f && z == 0f)
        {
            return;
        }

        // MoveAndCollide, not a position write: the shallows can be behind a rock or a jetty post,
        // and a push that tunnels the player into one is a worse trap than the water.
        player.MoveAndCollide(new Vector3(x, 0f, z) * push * dt);
    }

    private void TickActors(WorldHeightfield field)
    {
        foreach (Applied applied in _applied.Values)
        {
            applied.Seen = false;
        }

        SceneTree tree = GetTree();
        Sweep(tree, ObjectiveLocator.EnemyGroup, field);
        Sweep(tree, ObjectiveLocator.NpcGroup, field);

        // Anything not seen this sweep has left the groups (died, despawned, unloaded with its
        // cell). Strip what is still alive; forget what is not.
        List<ulong>? gone = null;
        foreach ((ulong id, Applied applied) in _applied)
        {
            if (applied.Seen || id == _playerId)
            {
                continue;
            }
            Strip(applied);
            (gone ??= new List<ulong>()).Add(id);
        }
        if (gone != null)
        {
            foreach (ulong id in gone)
            {
                _applied.Remove(id);
            }
        }
    }

    private void Sweep(SceneTree tree, string group, WorldHeightfield field)
    {
        foreach (Node node in tree.GetNodesInGroup(group))
        {
            if (node is not CharacterEntity actor || actor is PlayerCharacter || !IsInstanceValid(actor) ||
                actor.GetComponent<StatsComponent>() is not { } stats)
            {
                continue;
            }
            Vector3 feet = actor.GlobalPosition;
            bool flying = actor.GetComponent<LocomotionComponent>() is { Flying: true };
            float scale = flying
                ? 1f
                : WadingRules.QuantizedSpeedScale(
                    WorldWater.WadingDepthAt(feet.X, feet.Y, feet.Z, field), _tuning);
            Apply(actor, stats, scale, seen: true);
        }
    }

    /// <summary>Puts one body's wading modifier at <paramref name="scale"/>, touching the stat only
    /// when the quantised scale actually changed.</summary>
    private void Apply(Node body, StatsComponent stats, float scale, bool seen)
    {
        ulong id = body.GetInstanceId();
        if (!_applied.TryGetValue(id, out Applied? applied))
        {
            if (scale >= 1f)
            {
                return;
            }
            applied = new Applied { Stats = stats };
            _applied[id] = applied;
        }

        applied.Seen = seen || applied.Seen;
        if (Mathf.IsEqualApprox(applied.Scale, scale))
        {
            return;
        }

        Strip(applied);
        if (scale < 1f)
        {
            applied.Modifier = new StatModifier(scale - 1f, ModifierType.PercentMult, this);
            applied.Stats.GetStat(StatType.MoveSpeed).AddModifier(applied.Modifier);
            applied.Scale = scale;
        }
    }

    private static void Strip(Applied applied)
    {
        if (applied.Modifier != null && IsInstanceValid(applied.Stats))
        {
            applied.Stats.GetStat(StatType.MoveSpeed).RemoveModifier(applied.Modifier);
        }
        applied.Modifier = null;
        applied.Scale = 1f;
    }
}
