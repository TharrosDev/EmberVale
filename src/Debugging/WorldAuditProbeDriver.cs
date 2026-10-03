using System;
using System.Collections.Generic;
using System.Reflection;
using Embervale.Bootstrap;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>Small native fixtures for tools/world_audit_probe.gd. They exercise real streaming
/// activation, actor initialization and physics without loading or generating a region.</summary>
public partial class WorldAuditProbeDriver : RefCounted
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string CellId = "world_audit.near";
    private static readonly Vector3 PhysicsPoint = new(10000f, 0f, 10000f);
    private readonly List<Resource> _resources = new();
    private readonly List<string> _issues = new();
    private GameSession _session = null!;
    private RegionStreamer _streamer = null!;
    private PlayerCharacter _player = null!;
    private WorldCellActivation _activation = null!;
    private Node3D _cellRoot = null!;
    private Node3D _transient = null!;
    private Node3D _retiredBeforeTierChange = null!;
    private StaticBody3D _terrain = null!;
    private MeshInstance3D _survivingVisual = null!;
    private Node3D _physicsContext = null!;
    private WorldEventDirector _events = null!;

    public int CheckCount { get; private set; }

    public string[] Begin(Node3D parent)
    {
        _issues.Clear();
        CheckCount = 0;
        _session = new GameSession();
        parent.AddChild(_session);
        _player = new PlayerCharacter { Position = new Vector3(590f, 0f, 0f) };
        _session.Players.AddChild(_player);
        ServiceScope.RegisterOwned(_player, _player);
        _streamer = new RegionStreamer();
        _session.World.AddChild(_streamer);
        _streamer.SetProcess(false);
        ServiceScope.RegisterOwned(_streamer, _streamer);
        CheckPendingPriority();
        CheckInvalidSceneRoot();
        CheckMissingPreparedRegion();
        CheckActivationAfterRemoval();
        CheckTransientOwnership();
        _physicsContext = new Node3D { Position = PhysicsPoint };
        _session.World.AddChild(_physicsContext);
        _events = new WorldEventDirector();
        _session.World.AddChild(_events);
        _events.SetProcess(false);
        return TakeIssues();
    }

    private void CheckPendingPriority()
    {
        var nearSpawn = Keep(new RegionCellResource { Id = "world_audit.old", Center = new Vector3(500f, 0f, 0f) });
        var nearPlayer = Keep(new RegionCellResource { Id = CellId, Center = new Vector3(600f, 0f, 0f) });
        List<RegionCellResource> cells = Field<List<RegionCellResource>>(_streamer, "_cells");
        cells.Add(nearSpawn);
        cells.Add(nearPlayer);
        List<RegionCellResource> pending = Field<List<RegionCellResource>>(_streamer, "_pending");
        pending.Add(nearSpawn);
        pending.Add(nearPlayer);
        Invoke(_streamer, "RefreshDesiredTiers", false);
        Check(pending[0].Id == nearPlayer.Id, "load priority used the region spawn instead of the live player");
        _streamer.SetStreamingFocus(new Vector3(490f, 0f, 0f));
        Check(pending[0].Id == nearSpawn.Id, "tool focus did not update pending load priority");
        _streamer.RequirePosition(nearPlayer.Center);
        Check(pending[0].Id == nearPlayer.Id, "required landing did not outrank another Near cell");
        _streamer.ReleaseRequiredPosition();
        _streamer.ClearStreamingFocus();
        pending.Clear();
        // Tool focus deliberately enables streaming. This fixture drives stages directly;
        // prevent its synthetic cells with no scene path from entering the background loader.
        _streamer.SetProcess(false);
    }

    private void CheckInvalidSceneRoot()
    {
        var invalid = new Node();
        PackedScene scene = Keep(new PackedScene());
        Check(scene.Pack(invalid) == Error.Ok, "invalid-root fixture could not be packed");
        invalid.Free();
        RegionCellResource cell = Keep(new RegionCellResource { Id = "world_audit.invalid", ScenePath = "fixture" });
        int orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Invoke(_streamer, "Instantiate", cell, scene);
        }
        Check(_streamer.HasFailedCells(), "a non-Node3D cell did not exhaust its bounded retry policy");
        Check(!_streamer.IsSettled(), "a failed cell allowed the region to settle");
        Check((int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == orphans,
            "invalid cell instantiation leaked its rejected root");
        Invoke(_streamer, "ClearLoadStages");
    }

    private void CheckMissingPreparedRegion()
    {
        var region = Keep(new RegionResource
        {
            Id = "region.__world_audit_missing",
            EnvironmentProfile = Keep(new WorldEnvironmentProfileResource()),
        });
        region.Cells.Add(Keep(new RegionCellResource { Id = "world_audit.must_not_load", ScenePath = "fixture" }));
        var missing = new RegionStreamer();
        _session.World.AddChild(missing);
        missing.Configure(region);
        missing.SetProcess(false);
        Check(missing.HasFailedCells() && !missing.IsSettled(),
            "missing prepared data did not remain blocked without live generation");
        Check(Field<List<RegionCellResource>>(missing, "_cells").Count == 0 && missing.ResidentCellCount() == 0,
            "missing prepared data admitted raw source cells into production activation");
        missing.Free();
    }

    private void CheckActivationAfterRemoval()
    {
        _cellRoot = new Node3D { Position = new Vector3(600f, 0f, 0f) };
        _terrain = new StaticBody3D { Name = "TerrainCollider", CollisionLayer = CombatLayers.WorldStatic };
        _cellRoot.AddChild(_terrain);
        _survivingVisual = new MeshInstance3D { Name = "SurfaceSkin" };
        _cellRoot.AddChild(_survivingVisual);
        var removedBody = new CharacterBody3D { CollisionLayer = CombatLayers.WorldDynamic };
        removedBody.AddChild(new MeshInstance3D());
        _cellRoot.AddChild(removedBody);
        var removedNavigation = new NavigationRegion3D();
        _cellRoot.AddChild(removedNavigation);
        _activation = new WorldCellActivation(_cellRoot);
        _session.World.AddChild(_cellRoot);
        CompleteTier(WorldStreamingTier.Near);
        removedBody.Free();
        removedNavigation.Free();
        CompleteTier(WorldStreamingTier.Far);
        Check(_terrain.CollisionLayer == 0u, "Far retained the surviving terrain's collision");
        CompleteTier(WorldStreamingTier.Near);
        Check(_activation.HasTerrainCollision() && _activation.HasNavigation() && _survivingVisual.Visible,
            "freed descendants broke reactivation of surviving collision/visuals");
        Field<Dictionary<string, Node3D>>(_streamer, "_loaded")[CellId] = _cellRoot;
        Field<Dictionary<string, WorldCellActivation>>(_streamer, "_runtime")[CellId] = _activation;
        Field<HashSet<string>>(_streamer, "_gameplayActive").Add(CellId);
    }

    private void CheckTransientOwnership()
    {
        Vector3 desired = new(603f, 2f, 4f);
        Vector3 initializedAt = Vector3.Inf;
        _transient = new Node3D { Position = desired };
        _transient.Ready += () => initializedAt = _transient.GlobalPosition;
        Check(_streamer.TryAddCellOwnedActor(_transient, desired), "Near cell refused its transient actor");
        Check(_transient.GlobalPosition.IsEqualApprox(desired), "cell parent offset moved a world-space actor");
        Check(initializedAt.IsEqualApprox(desired), "actor initialization observed an incorrect world position");
        _retiredBeforeTierChange = new Node3D();
        Check(_streamer.TryAddCellOwnedActor(_retiredBeforeTierChange, desired), "Near cell refused removal fixture");
        _retiredBeforeTierChange.Free();
        Check(Field<List<Node3D>>(_activation, "_transientActors").Count == 1,
            "cell retained the handle of an actor that already exited");
        Invoke(_streamer, "ScheduleTier", CellId, WorldStreamingTier.Mid);
        Check(_transient.IsQueuedForDeletion(), "leaving Near did not retire the transient actor");
        CompleteTier(WorldStreamingTier.Mid);
        var refused = new Node3D();
        Check(!_streamer.TryAddCellOwnedActor(refused, desired), "Mid accepted a gameplay actor");
        refused.Free();
    }

    public string[] CheckRetirement()
    {
        Check(!GodotObject.IsInstanceValid(_transient), "cell-owned actor survived cleanup after leaving Near");
        return TakeIssues();
    }

    public string[] CheckNoGround()
    {
        Check(!SpawnPlacement.TryResolve(_physicsContext, PhysicsPoint, out _),
            "actor placement accepted analytic ground with no real collider");
        var raid = Keep(new WorldEventResource { EnemyTemplateId = "enemy.__must_not_instantiate" });
        var worldEvent = new WorldEvent(raid, PhysicsPoint, 2, 60d);
        Check(!(bool)Invoke(_events, "Materialize", worldEvent)!, "raid with no safe landing reported materialization success");
        Check(worldEvent.Actors.Count == 0 && worldEvent.Enemies.Count == 0 && worldEvent.EnemyIds.Count == 0,
            "failed raid retained actors or objective ids");
        var item = Keep(new ItemResource { Id = "item.__world_audit" });
        var items = (Dictionary<string, ItemResource>)typeof(ItemDatabase)
            .GetField("ById", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        items.Add(item.Id, item);
        var cache = Keep(new WorldEventResource { Kind = WorldEventKind.Cache, CacheItemId = item.Id });
        var cacheEvent = new WorldEvent(cache, PhysicsPoint, 1, 60d);
        try
        {
            Check(!(bool)Invoke(_events, "Materialize", cacheEvent)!, "cache with no safe landing reported materialization success");
        Check(cacheEvent.Actors.Count == 0, "failed cache retained a pickup");
        }
        finally
        {
            items.Remove(item.Id);
        }
        raid.Id = "event.__world_audit";
        raid.MinCount = raid.MaxCount = 1;
        raid.SpawnDistanceMin = raid.SpawnDistanceMax = 0f;
        var events = (Dictionary<string, WorldEventResource>)typeof(WorldEventDatabase)
            .GetField("ById", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        events.Add(raid.Id, raid);
        try
        {
            Check(!_events.ForceStart(raid.Id) && _events.Active == null,
                "forced event reported success when no actors could be materialized");
        }
        finally
        {
            events.Remove(raid.Id);
        }
        return TakeIssues();
    }

    public void AddFloor()
    {
        _ = Box(new Vector3(20f, 1f, 20f), PhysicsPoint + new Vector3(0f, -0.5f, 0f));
    }

    public string[] CheckClearGround()
    {
        Check(SpawnPlacement.TryResolve(_physicsContext, PhysicsPoint, out Vector3 resolved),
            "real unobstructed floor refused actor placement");
        Check(Mathf.Abs(resolved.Y - 0.96f) < 0.03f, "actor landing did not retain the validated capsule clearance");
        return TakeIssues();
    }

    public void AddBlocker()
    {
        _ = Box(new Vector3(20f, 21f, 20f), PhysicsPoint + new Vector3(0f, 9.5f, 0f));
    }

    public string[] CheckBlockedGround()
    {
        Check(!SpawnPlacement.TryResolve(_physicsContext, PhysicsPoint, out _),
            "blocked ring search fell back to an unchecked analytic actor landing");
        return TakeIssues();
    }

    public void BeginPlayerLoading(Node3D parent)
    {
        // Finish() has already closed the original session/world scopes. The placement geometry
        // has application ownership here, so each player session can be opened and destroyed
        // independently without overlapping session/world scopes or deleting the next fixture.
        _physicsContext = new Node3D { Name = "WorldAuditPlayerPhysics" };
        parent.AddChild(_physicsContext);
        _ = Box(new Vector3(20f, 1f, 20f), PhysicsPoint + new Vector3(0f, -0.5f, 0f), _physicsContext);
    }

    public string[] CheckClearPlayerLoading()
    {
        CheckPlayerLoading(blocked: false);
        return TakeIssues();
    }

    public void AddPlayerLoadingBlocker()
    {
        _ = Box(new Vector3(20f, 21f, 20f), PhysicsPoint + new Vector3(0f, 9.5f, 0f), _physicsContext);
    }

    public string[] CheckBlockedPlayerLoading()
    {
        CheckPlayerLoading(blocked: true);
        return TakeIssues();
    }

    private void CheckPlayerLoading(bool blocked)
    {
        // Each gate has its own real lifecycle; the earlier streaming fixture is already freed.
        GameState previousState = GameManager.Instance.State;
        var lifecycle = new SessionLifecycleCoordinator();
        _physicsContext.GetTree().Root.AddChild(lifecycle);
        var session = new GameSession { Lifecycle = lifecycle };
        lifecycle.AddChild(session);
        typeof(SessionLifecycleCoordinator).GetProperty(nameof(SessionLifecycleCoordinator.Session))!
            .SetValue(lifecycle, session);
        var player = new PlayerCharacter { Position = PhysicsPoint + Vector3.Up };
        session.Players.AddChild(player);
        player.SetProcess(false);
        player.SetPhysicsProcess(false);
        typeof(PlayerHost).GetProperty(nameof(PlayerHost.Player))!.SetValue(session.Players, player);
        LoadingCoordinator loading = session.Loading;
        loading.PlacementRetryFrames = 3;
        int completions = 0;
        loading.Begin("world audit player landing", () => completions++);
        loading.SetPhysicsProcess(false); // the fixture drives the real tick inside physics frames
        try
        {
            if (!blocked)
            {
                loading._PhysicsProcess(1d / 60d);
                Check(lifecycle.HasSession && GameManager.Instance.IsPlaying && completions == 1,
                    "unobstructed player loading did not enter Playing with one completion");
                Check(Mathf.Abs(player.GlobalPosition.Y - 0.96f) < 0.03f,
                    "player loading did not use the validated capsule landing");
                loading._PhysicsProcess(1d / 60d);
                Check(completions == 1, "settled player loading repeated its completion");
            }
            else
            {
                for (int attempt = 1; attempt < loading.PlacementRetryFrames; attempt++)
                {
                    loading._PhysicsProcess(1d / 60d);
                    Check(lifecycle.HasSession && GameManager.Instance.State == GameState.Loading && completions == 0,
                        "blocked player loading released play or aborted before its retry budget");
                    Check(Field<int>(loading, "_placementAttempts") == attempt,
                        "blocked player loading did not retry a real capsule search");
                }
                loading._PhysicsProcess(1d / 60d);
                Check(!lifecycle.HasSession && GameManager.Instance.State == GameState.MainMenu && completions == 0,
                    "exhausted player landing resumed play instead of destroying the unsafe session");
                Check(Field<Action?>(loading, "_onSettled") == null && Field<double>(loading, "_elapsed") < 0d,
                    "aborted player landing retained its completion or a live loading gate");
            }
        }
        finally
        {
            lifecycle.DestroySession();
            lifecycle.Free();
            GameManager.Instance.ChangeState(previousState);
        }
    }

    private StaticBody3D Box(Vector3 size, Vector3 position, Node3D? parent = null)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = CombatLayers.WorldStatic };
        body.AddChild(new CollisionShape3D { Shape = Keep(new BoxShape3D { Size = size }) });
        (parent ?? _session.World).AddChild(body);
        return body;
    }

    private void CompleteTier(WorldStreamingTier tier)
    {
        _activation.TargetTier = tier;
        _activation.Stage = 0;
        for (int step = 0; step < 4 && !_activation.Advance(); step++) { }
        Check(_activation.Tier == tier, $"activation did not complete {tier}");
    }

    private T Keep<T>(T resource) where T : Resource
    {
        _resources.Add(resource);
        return resource;
    }

    private void Check(bool passed, string message)
    {
        CheckCount++;
        if (!passed) _issues.Add(message);
    }

    private string[] TakeIssues()
    {
        string[] issues = _issues.ToArray();
        _issues.Clear();
        return issues;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static object? Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, arguments);

    public void Finish()
    {
        _streamer.UnloadAll();
        _session.Free();
        foreach (Resource resource in _resources) resource.Dispose();
        _resources.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void FinishPlayerLoading()
    {
        _physicsContext.Free();
        foreach (Resource resource in _resources) resource.Dispose();
        _resources.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
