using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// A scripted raid or ambush, authored as a node in a cell <c>.tscn</c> (campaign overhaul): spawns
/// <see cref="Waves"/> waves of <see cref="CountPerWave"/> enemies drawn round-robin from
/// <see cref="TemplateIds"/>, placed on real ground through <see cref="SpawnPlacement"/> (the
/// <see cref="SafePlacementService"/> contract) at child <c>Marker3D</c> points, nodes in
/// <see cref="SpawnMarkerGroup"/> under the same cell, or a ring of <see cref="SpawnRadius"/> round this
/// node. A Defend objective needs no kills, so this component supplies the pressure while it runs.
///
/// <para><b>Trigger.</b> With <see cref="TriggerFlagId"/> empty it fires when the player is inside a box of
/// <see cref="TriggerSize"/> centred on this node (rotated with it), polled at 4 Hz; with the flag set it
/// fires when that story flag is held (checked on every flag change and when the node enters the world, so
/// a cell that streams in after the flag still fires). Either way <see cref="RequiredFlagId"/>, when set,
/// must be held too. ⚠️ The box is a containment test against the player's position rather than an
/// <c>Area3D</c>: the player's body sits on the default physics layer, not
/// <c>CombatLayers.Player</c>, so a layer-filtered area cannot tell the player from terrain, and a
/// position test also answers "still inside" after a load for free.</para>
///
/// <para><b>Ending.</b> When every wave has been released, placed and killed it sets
/// <see cref="ClearedFlagId"/> (always author one: it is the durable record, see below). Setting
/// <see cref="DespawnFlagId"/> removes whatever is alive and stops the piece without clearing it.</para>
///
/// <para><b>Persistence.</b> It is an <see cref="ISaveable"/> keyed by <see cref="SaveId"/>, built from the
/// owning cell scene's path and this node's path inside it (stable across sessions). It saves two facts,
/// <c>fired</c> and <c>cleared</c>; the enemies it spawned are transient and are not saved. A save where it
/// fired and did not clear restarts the piece when its trigger still holds. ⚠️ A scene-node saveable is
/// only written while its cell is resident, so <see cref="ClearedFlagId"/> (a story flag, saved with the
/// player) is also the truth: a held cleared flag always means cleared.</para>
/// </summary>
[GlobalClass]
public partial class SetPieceSpawnComponent : Node3D, ISaveable
{
    /// <summary>Spawn period of a failed placement: owed spawns are retried this often.</summary>
    private const float RetrySeconds = 1f;

    /// <summary>Story flag that fires the piece; empty means "player enters the trigger area".</summary>
    [Export] public string TriggerFlagId { get; set; } = string.Empty;

    /// <summary>Flag that must be held before the piece may fire (either trigger kind).</summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;

    /// <summary>Size of the trigger box (used only when <see cref="TriggerFlagId"/> is empty).</summary>
    [Export] public Vector3 TriggerSize { get; set; } = new(24f, 8f, 24f);

    /// <summary>Enemy template ids (<c>enemy.*</c>), drawn round-robin.</summary>
    [Export] public string[] TemplateIds { get; set; } = System.Array.Empty<string>();

    [Export] public int CountPerWave { get; set; } = 3;

    [Export] public int Waves { get; set; } = 1;

    /// <summary>Seconds between wave releases.</summary>
    [Export] public float WaveDelaySeconds { get; set; } = 15f;

    /// <summary>Optional group whose <c>Node3D</c> members, under this node's parent, are spawn points.
    /// Child <c>Marker3D</c> nodes of this component are always spawn points.</summary>
    [Export] public string SpawnMarkerGroup { get; set; } = string.Empty;

    /// <summary>Ring radius used when no spawn marker exists.</summary>
    [Export] public float SpawnRadius { get; set; } = 8f;

    /// <summary>Flag set when every spawned enemy has died and every wave has been released.</summary>
    [Export] public string ClearedFlagId { get; set; } = string.Empty;

    /// <summary>When this flag is held the live enemies are removed and the piece stops.</summary>
    [Export] public string DespawnFlagId { get; set; } = string.Empty;

    /// <summary>The piece has been triggered (persisted).</summary>
    public bool Fired { get; private set; }

    /// <summary>The piece has been beaten (persisted, and re-derived from <see cref="ClearedFlagId"/>).</summary>
    public bool Cleared { get; private set; }

    /// <summary>Enemies currently alive from this piece.</summary>
    public int AliveCount => _alive.Count;

    /// <summary>True while waves are being released or enemies are alive.</summary>
    public bool IsRunning => _waves is { Started: true } && !Cleared && !_stopped;

    public string SaveId
    {
        get
        {
            Node? scope = Owner ?? GetParent();
            string cell = Owner?.SceneFilePath ?? string.Empty;
            string path = scope != null && scope.IsAncestorOf(this) ? scope.GetPathTo(this).ToString() : Name.ToString();
            return $"setpiece:{cell}#{path}";
        }
    }

    private SetPieceWaves? _waves;
    private readonly HashSet<ulong> _alive = new();
    private readonly List<EnemyEntity> _enemies = new();
    private const float PollSeconds = 0.25f;
    private float _sincePoll;
    private bool _playerInside;
    private bool _stopped;
    private float _retry;
    private int _spawnCounter;
    private bool _warnedPlacement;

    public override void _EnterTree()
    {
        SaveManager.Instance?.Register(this);
    }

    public override void _Ready()
    {
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);

        // Deferred: the node enters while its cell is still being assembled, and the flag state is
        // only meaningful once the player exists.
        CallDeferred(nameof(Evaluate));
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlag);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        SaveManager.Instance?.Unregister(this);
    }

    private void OnFlag(StoryFlagChangedEvent e) => Evaluate();

    private void OnGameLoaded(GameLoadedEvent e) => CallDeferred(nameof(Evaluate));

    /// <summary>Fires the piece when it is eligible. Public so a harness or dev command can poke it.</summary>
    public void Evaluate()
    {
        if (!IsInsideTree() || Flags() is not { } flags)
        {
            return;
        }

        if (ClearedFlagId.Length > 0 && flags.Has(ClearedFlagId))
        {
            Cleared = true;
        }

        if (DespawnFlagId.Length > 0 && flags.Has(DespawnFlagId))
        {
            Stop();
            return;
        }

        if (Cleared || _stopped || (_waves?.Started ?? false))
        {
            return;
        }

        if (RequiredFlagId.Length > 0 && !flags.Has(RequiredFlagId))
        {
            return;
        }

        bool trigger = TriggerFlagId.Length > 0 ? flags.Has(TriggerFlagId) : _playerInside;
        if (trigger)
        {
            Begin();
        }
    }

    /// <summary>Starts the waves now, ignoring the trigger (harness / dev). Still honours cleared and stopped.</summary>
    public void Begin()
    {
        if (Cleared || _stopped || (_waves?.Started ?? false))
        {
            return;
        }

        _waves = new SetPieceWaves(Waves, CountPerWave, WaveDelaySeconds);
        _waves.Start();
        Fired = true;
        _retry = 0f;
        Log.Info($"Set piece '{SaveId}' started ({_waves.Waves} wave(s) of {_waves.CountPerWave}).");
    }

    /// <summary>Polls the player against the trigger box (area-triggered pieces only).</summary>
    private void PollTrigger(float delta)
    {
        if (TriggerFlagId.Length > 0 || Cleared || _stopped || (_waves?.Started ?? false))
        {
            return;
        }

        _sincePoll += delta;
        if (_sincePoll < PollSeconds)
        {
            return;
        }

        _sincePoll = 0f;
        if (!IsInsideTree() || ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return;
        }

        Vector3 local = GlobalTransform.AffineInverse() * player.GlobalPosition;
        _playerInside = SetPieceWaves.InsideBox(local.X, local.Y, local.Z, TriggerSize.X, TriggerSize.Y, TriggerSize.Z);
        if (_playerInside)
        {
            Evaluate();
        }
    }

    public override void _Process(double delta)
    {
        PollTrigger((float)delta);
        if (_waves is not { Started: true } waves || Cleared || _stopped)
        {
            return;
        }

        if (DespawnFlagId.Length > 0 && Flags()?.Has(DespawnFlagId) == true)
        {
            Stop();
            return;
        }

        int owed = waves.Tick((float)delta);
        if (owed > 0)
        {
            _retry -= (float)delta;
            if (_retry <= 0f)
            {
                _retry = RetrySeconds;
                SpawnOwed(waves, owed);
            }
        }

        if (waves.Cleared)
        {
            Complete();
        }
    }

    private void SpawnOwed(SetPieceWaves waves, int owed)
    {
        if (TemplateIds.Length == 0)
        {
            Log.Warn($"Set piece '{SaveId}' has no TemplateIds; nothing can spawn.");
            return;
        }

        List<Vector3> markers = SpawnPoints();
        int placed = 0;
        for (int i = 0; i < owed; i++)
        {
            string template = TemplateIds[_spawnCounter % TemplateIds.Length];
            if (!EnemyTemplateRegistry.IsRegistered(template))
            {
                Log.Warn($"Set piece '{SaveId}': '{template}' is not a registered enemy template.");
                break;
            }

            Vector3 desired = PointFor(markers, _spawnCounter);
            _spawnCounter++;
            if (!SpawnPlacement.TryResolve(this, desired, out Vector3 position))
            {
                if (!_warnedPlacement)
                {
                    _warnedPlacement = true;
                    Log.Warn($"Set piece '{SaveId}': no safe placement near {desired}; retrying until the ground is ready.");
                }

                continue;
            }

            EnemyEntity enemy = EnemyTemplateRegistry.Create(template, position);
            if (!TryOwn(enemy, position))
            {
                enemy.Free();
                continue;
            }

            ulong id = enemy.RuntimeId;
            _alive.Add(id);
            _enemies.Add(enemy);
            enemy.TreeExited += () => OnGone(id);
            placed++;
        }

        waves.Spawned(placed);
    }

    private bool TryOwn(EnemyEntity enemy, Vector3 position)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer))
        {
            return streamer.TryAddCellOwnedActor(enemy, position);
        }

        // No streamer (a harness or a bare scene): the set piece's own parent owns them.
        if (GetParent() is not { } parent)
        {
            return false;
        }

        parent.AddChild(enemy);
        enemy.GlobalPosition = position;
        return true;
    }

    private List<Vector3> SpawnPoints()
    {
        var points = new List<Vector3>();
        foreach (Node child in GetChildren())
        {
            if (child is Marker3D marker)
            {
                points.Add(marker.GlobalPosition);
            }
        }

        if (SpawnMarkerGroup.Length > 0 && GetParent() is { } parent)
        {
            foreach (Node node in GetTree().GetNodesInGroup(SpawnMarkerGroup))
            {
                if (node is Node3D marker && parent.IsAncestorOf(marker))
                {
                    points.Add(marker.GlobalPosition);
                }
            }
        }

        return points;
    }

    private Vector3 PointFor(List<Vector3> markers, int index)
    {
        if (markers.Count > 0)
        {
            return markers[index % markers.Count];
        }

        // A ring round this node, advancing by the golden angle so consecutive spawns do not stack.
        float angle = index * 2.3999632f;
        return GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnRadius;
    }

    private void OnDied(EntityDiedEvent e) => OnGone(e.Entity.RuntimeId);

    private void OnGone(ulong runtimeId)
    {
        // Death and the corpse's later removal both land here; only the first counts.
        if (!_alive.Remove(runtimeId))
        {
            return;
        }

        _waves?.Died();
    }

    private void Complete()
    {
        Cleared = true;
        _enemies.Clear();
        Log.Info($"Set piece '{SaveId}' cleared.");
        if (ClearedFlagId.Length > 0)
        {
            Flags()?.Set(ClearedFlagId);
        }
    }

    /// <summary>Removes the live enemies and stops releasing waves (despawn flag, or a load).</summary>
    private void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        FreeSpawned();
    }

    private void FreeSpawned()
    {
        foreach (EnemyEntity enemy in _enemies)
        {
            if (IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
            {
                enemy.QueueFree();
            }
        }

        _enemies.Clear();
        _alive.Clear();
    }

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save() => new() { ["fired"] = Fired, ["cleared"] = Cleared };

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replace, never merge: whatever this piece spawned in the abandoned timeline goes, and the
        // waves restart from nothing when the loaded state says it fired and did not clear.
        FreeSpawned();
        _waves = null;
        _stopped = false;
        _retry = 0f;
        _spawnCounter = 0;
        _warnedPlacement = false;
        Fired = data.TryGetValue("fired", out Variant fired) && fired.AsBool();
        Cleared = data.TryGetValue("cleared", out Variant cleared) && cleared.AsBool();

        // Fired and not cleared: restart when the trigger still holds (re-checked deferred, after the
        // flags and the player have loaded).
        CallDeferred(nameof(Evaluate));
    }
}
