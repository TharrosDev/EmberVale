using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Periodically validates the world's runtime invariants and reports any breakage: a standing
/// sanity net that catches "impossible" states close to where they happen. One pass checks the
/// player (registered, stats, inventory, finite position and velocity, a current camera, ground
/// under them in a settled world, resources within range, gold and stack counts), every live enemy
/// (finite position, not under the world), the streamer (no failed cells, a region id once
/// settled), the pause state, and orphan nodes beyond the pools.
///
/// <para>Runs on a timer in a developer session, on demand from the dev console
/// (<c>invariants</c>), and from the <c>--perf-report</c> session report, which turns the result
/// into an exit code. <see cref="Check"/> returns the findings as data; every finding also goes
/// through <see cref="Invariant.Check"/>, so the log and the violation counter stay the single
/// record.</para>
/// </summary>
[GlobalClass]
public partial class WorldIntegrityChecker : Node
{
    /// <summary>Below this height an actor has fallen out of the world. No realm's ground is
    /// within hundreds of metres of it.</summary>
    private const float WorldFloor = -500f;

    /// <summary>Seconds between automatic checks.</summary>
    [Export] public float Interval { get; set; } = 5f;

    private double _timer;

    public override void _Process(double delta)
    {
        _timer += delta;
        if (_timer < Interval)
        {
            return;
        }

        _timer = 0d;

        // The periodic pass only speaks up when something is wrong.
        int before = Invariant.Violations;
        Check();
        if (Invariant.Violations > before)
        {
            Log.Warn($"WorldIntegrityChecker: {Invariant.Violations - before} new invariant violation(s).");
        }
    }

    /// <summary>Runs every check once and returns a human-readable summary.</summary>
    public static string Run() => Check().ToText();

    /// <summary><see cref="Check"/> as one line of JSON. An instance method so a GDScript probe can
    /// call it on a node it made from this script.</summary>
    public string CheckJson() => Check().ToJson();

    /// <summary>Runs every check once and returns what it found, each issue with a stable code.</summary>
    public static IntegrityReport Check()
    {
        var report = new IntegrityReport();
        CheckPlayer(report);
        CheckEnemies(report);
        CheckStreaming(report);
        CheckPause(report);
        CheckOrphans(report);
        return report;
    }

    /// <summary>Counts the violation, logs it and records it in the report.</summary>
    private static bool Require(IntegrityReport report, bool ok, string code, string message)
    {
        if (!Invariant.Check(ok, message))
        {
            report.Add(code, message);
        }

        return ok;
    }

    private static void CheckPlayer(IntegrityReport report)
    {
        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            Require(report, false, "player.unregistered", "player is not registered in the ServiceLocator");
            return;
        }

        StatsComponent? stats = player.GetComponent<StatsComponent>();
        InventoryComponent? inventory = player.GetComponent<InventoryComponent>();
        Require(report, stats is not null, "player.no_stats", "player has no StatsComponent");
        Require(report, inventory is not null, "player.no_inventory", "player has no InventoryComponent");

        Vector3 pos = player.GlobalPosition;
        bool finite = Require(report, IsFinite(pos), "player.position", $"player position is not finite ({pos})");
        if (finite)
        {
            Require(report, pos.Y > WorldFloor, "player.fell", $"player is under the world at {pos}");
        }

        Vector3 velocity = player.Velocity;
        if (Require(report, IsFinite(velocity), "player.velocity", $"player velocity is not finite ({velocity})"))
        {
            Require(report, velocity.LengthSquared() < 250000f, "player.speed",
                $"player velocity is impossible ({velocity.Length():0.0} m/s)");
        }

        Require(report, player.GetComponent<PlayerCameraRig>()?.Camera is { Current: true }, "player.camera",
            "player has no current gameplay camera");

        if (finite && ServiceLocator.Instance.TryGet(out RegionStreamer streamer) && streamer.IsSettled())
        {
            var query = PhysicsRayQueryParameters3D.Create(pos + Vector3.Up, pos + Vector3.Down * 4f, 1u);
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
            bool grounded = player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
            Require(report, grounded, "player.no_ground",
                $"settled world has no collision within 4 m under player at {pos}");
        }

        if (stats is not null)
        {
            CheckResource(report, stats, StatType.Health);
            CheckResource(report, stats, StatType.Stamina);
            CheckResource(report, stats, StatType.Mana);
        }

        if (inventory is not null)
        {
            int gold = inventory.CountOf(GameIds.Currency.Gold);
            Require(report, gold >= 0, "player.gold", $"player gold is negative ({gold})");
            CheckStacks(report, inventory.Stacks, "pack");
            CheckStacks(report, inventory.Materials, "materials bag");
        }
    }

    /// <summary>A stack that reached zero should have been removed; one that stays is an item the
    /// player can see and cannot use.</summary>
    private static void CheckStacks(
        IntegrityReport report, System.Collections.Generic.IReadOnlyList<ItemStack> stacks, string where)
    {
        foreach (ItemStack stack in stacks)
        {
            if (stack.Quantity <= 0)
            {
                Require(report, false, "player.empty_stack",
                    $"{where} holds a stack of {stack.Quantity} x {stack.Item.Id}");
                return; // One is enough to say the inventory is wrong; do not log every slot.
            }
        }
    }

    /// <summary>Every live enemy has a real position above the world. Reported once per pass with a
    /// count, so a broken spawner does not write a line per goblin.</summary>
    private static void CheckEnemies(IntegrityReport report)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        int notFinite = 0;
        int fallen = 0;
        string example = string.Empty;
        foreach (Node node in tree.GetNodesInGroup(ObjectiveLocator.EnemyGroup))
        {
            if (node is not Node3D body || !body.IsInsideTree() || body.IsQueuedForDeletion())
            {
                continue;
            }

            Vector3 at = body.GlobalPosition;
            if (!IsFinite(at))
            {
                notFinite++;
                example = body.Name;
            }
            else if (at.Y <= WorldFloor)
            {
                fallen++;
                example = body.Name;
            }
        }

        Require(report, notFinite == 0, "enemy.position",
            $"{notFinite} enemy position(s) are not finite (e.g. {example})");
        Require(report, fallen == 0, "enemy.fell", $"{fallen} enemy(ies) are under the world (e.g. {example})");
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void CheckStreaming(IntegrityReport report)
    {
        if (ServiceLocator.Instance is null || !ServiceLocator.Instance.TryGet(out RegionStreamer streamer))
        {
            return; // Main menu and validation runs have no active world by design.
        }

        Require(report, !streamer.HasFailedCells(), "streaming.failed_cells",
            $"active region '{streamer.ActiveRegionId}' has failed streaming cells");
        if (streamer.IsSettled())
        {
            Require(report, !string.IsNullOrWhiteSpace(streamer.ActiveRegionId), "streaming.no_region",
                "streamer settled without an active region id");
        }
    }

    /// <summary>The pause deadlock in one line: the game says it is paused and the tree is not, so
    /// the world keeps simulating behind a pause menu.</summary>
    private static void CheckPause(IntegrityReport report)
    {
        if (GameManager.Instance is { State: GameState.Paused } && Engine.GetMainLoop() is SceneTree tree)
        {
            Require(report, tree.Paused, "pause.tree_running", "the game state is Paused but the scene tree is not");
        }
    }

    private static void CheckResource(IntegrityReport report, StatsComponent stats, StatType type)
    {
        float current = stats.GetCurrent(type);
        float max = stats.GetMax(type);
        bool ok = current >= -0.01f && current <= max + 0.01f && max >= 0f;
        Require(report, ok, "player.resource", $"{type} current {current:0.##} out of range [0, {max:0.##}]");
    }

    private static void CheckOrphans(IntegrityReport report)
    {
        // Nodes parked in a NodePool are detached from the tree on purpose (the pool's working
        // set) and so register as Godot "orphan nodes" without being leaks. Subtract them so the
        // invariant flags only the excess — a genuine leak — not the pool.
        var orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        int pooled = NodePoolCensus.Parked;
        int leaked = orphans - pooled;
        Require(report, leaked <= 0, "orphans.leaked",
            $"{leaked} orphan node(s) leaked (orphans={orphans}, pooled={pooled})");
    }
}
