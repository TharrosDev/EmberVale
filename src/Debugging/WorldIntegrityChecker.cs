using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Player;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Periodically validates the world's runtime invariants and reports any breakage — a
/// standing sanity net that catches "impossible" states (a player with no stats, a resource
/// above its max, a NaN position, leaked orphan nodes) close to where they happen. Runs on a
/// timer and is also invokable on demand from the dev console (<c>invariants</c>).
/// </summary>
[GlobalClass]
public partial class WorldIntegrityChecker : Node
{
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
        Run();
        if (Invariant.Violations > before)
        {
            Log.Warn($"WorldIntegrityChecker: {Invariant.Violations - before} new invariant violation(s).");
        }
    }

    /// <summary>Runs every check once and returns a human-readable summary.</summary>
    public static string Run()
    {
        var sb = new StringBuilder();
        int before = Invariant.Violations;

        CheckPlayer(sb);
        CheckStreaming(sb);
        CheckOrphans(sb);

        int found = Invariant.Violations - before;
        return found == 0 ? "Integrity OK." + sb : $"Integrity: {found} issue(s).\n{sb}";
    }

    private static void CheckPlayer(StringBuilder sb)
    {
        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            sb.Append("• player not registered\n");
            Invariant.Check(false, "player is not registered in the ServiceLocator");
            return;
        }

        if (!Invariant.Check(player.GetComponent<StatsComponent>() is not null, "player has no StatsComponent"))
        {
            sb.Append("• player missing StatsComponent\n");
        }

        if (!Invariant.Check(player.GetComponent<InventoryComponent>() is not null, "player has no InventoryComponent"))
        {
            sb.Append("• player missing InventoryComponent\n");
        }

        Vector3 pos = player.GlobalPosition;
        bool finite = !(float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsNaN(pos.Z) ||
                        float.IsInfinity(pos.X) || float.IsInfinity(pos.Y) || float.IsInfinity(pos.Z));
        if (!Invariant.Check(finite, $"player position is not finite ({pos})"))
        {
            sb.Append("• player position not finite\n");
        }

        Vector3 velocity = player.Velocity;
        bool velocityFinite = IsFinite(velocity);
        if (!Invariant.Check(velocityFinite, $"player velocity is not finite ({velocity})"))
            sb.Append("• player velocity not finite\n");
        else if (!Invariant.Check(velocity.LengthSquared() < 250000f,
                     $"player velocity is impossible ({velocity.Length():0.0} m/s)"))
            sb.Append($"• impossible player velocity ({velocity.Length():0.0} m/s)\n");

        if (!Invariant.Check(player.GetComponent<PlayerCameraRig>()?.Camera is { Current: true },
                             "player has no current gameplay camera"))
            sb.Append("• player camera missing/not current\n");

        if (ServiceLocator.Instance.TryGet(out RegionStreamer streamer) && streamer.IsSettled())
        {
            var query = PhysicsRayQueryParameters3D.Create(pos + Vector3.Up, pos + Vector3.Down * 4f, 1u);
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
            bool grounded = player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
            if (!Invariant.Check(grounded, $"settled world has no collision within 4 m under player at {pos}"))
                sb.Append("• no collision under player in settled world\n");
        }

        if (player.GetComponent<StatsComponent>() is { } stats)
        {
            CheckResource(sb, stats, StatType.Health);
            CheckResource(sb, stats, StatType.Stamina);
            CheckResource(sb, stats, StatType.Mana);
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void CheckStreaming(StringBuilder sb)
    {
        if (ServiceLocator.Instance is null || !ServiceLocator.Instance.TryGet(out RegionStreamer streamer))
            return; // Main menu and validation runs have no active world by design.
        if (!Invariant.Check(!streamer.HasFailedCells(),
                             $"active region '{streamer.ActiveRegionId}' has failed streaming cells"))
            sb.Append($"• failed cells in {streamer.ActiveRegionId}\n");
        if (streamer.IsSettled() && !Invariant.Check(!string.IsNullOrWhiteSpace(streamer.ActiveRegionId),
                                                     "streamer settled without an active region id"))
            sb.Append("• settled streamer has no active region\n");
    }

    private static void CheckResource(StringBuilder sb, StatsComponent stats, StatType type)
    {
        float current = stats.GetCurrent(type);
        float max = stats.GetMax(type);
        bool ok = current >= -0.01f && current <= max + 0.01f && max >= 0f;
        if (!Invariant.Check(ok, $"{type} current {current:0.##} out of range [0, {max:0.##}]"))
        {
            sb.Append($"• {type} out of range ({current:0}/{max:0})\n");
        }
    }

    private static void CheckOrphans(StringBuilder sb)
    {
        // Nodes parked in a NodePool are detached from the tree on purpose (the pool's working
        // set) and so register as Godot "orphan nodes" without being leaks. Subtract them so the
        // invariant flags only the excess — a genuine leak — not the pool.
        var orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        int pooled = NodePoolCensus.Parked;
        int leaked = orphans - pooled;
        if (!Invariant.Check(leaked <= 0, $"{leaked} orphan node(s) leaked (orphans={orphans}, pooled={pooled})"))
        {
            sb.Append($"• {leaked} orphan node(s) leaked\n");
        }
    }
}
