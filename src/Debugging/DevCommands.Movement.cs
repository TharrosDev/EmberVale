using System.Collections.Generic;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Enemies;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Where the player is and how to put them, or an enemy, somewhere else: <c>pos</c>, <c>tp</c>,
/// <c>spawn</c>, <c>face</c>.
///
/// <para>Every teleport ends in <see cref="WorldSessionDirector.PerformRegionLoad"/>, the same hard
/// load a portal and fast travel use, so the loading gate holds play until the ground under the
/// landing is real. That makes <c>tp</c> asynchronous: the reply comes back while the game is in
/// <see cref="GameState.Loading"/>, and the script runner waits for it before the next statement.</para>
/// </summary>
public static partial class DevCommands
{
    /// <summary>How far above the ground a teleport sets the player's feet down.</summary>
    private const float TeleportClearance = 0.5f;

    private static void RegisterMovement(DevConsole console)
    {
        console.Register(new ConsoleCommand("pos", "pos", "Where the player is: region, cell, position, yaw, ground height, safe zone, water.", Pos));
        console.Register(new ConsoleCommand("tp", "tp <x> <z> [clearance]|cell <cellId>|loc <locationId>|region <regionId>|out [metres]", "Teleport through the real loading gate: to a world coordinate, a cell centre, a map location, a region's spawn, or the nearest dry ground outside every safe zone at least that far away (default 40).", Tp));
        console.Register(new ConsoleCommand("spawn", "spawn <templateId> [n] [ahead <m>|at <dx> <dz>] | spawn [n] [templateId] | spawn list [prefix]", "Spawn n enemies (1 to 50) on a fixed ring around a point ahead of the player (default 6 m) or at a world-axis offset, each on validated ground and owned by its cell; or list the templates.", Spawn));
        console.Register(new ConsoleCommand("face", "face <yawDegrees> [pitchDegrees]|nearest|at <x> <z>", "Turn the player (and the view): to a yaw (0 looks along -Z, positive turns left) and pitch (positive looks up), at the nearest live enemy, or at a world point.", Face));
    }

    private static string Pos(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player))
        {
            return console.Fail("no player");
        }

        Vector3 p = player.GlobalPosition;
        string regionId = TryService(out RegionStreamer streamer) ? streamer.ActiveRegionId : string.Empty;
        RegionCellResource? cell = CellNear(RegionDatabase.Get(regionId), p, out bool inside);
        float ground = WorldGround.HeightAt(p.X, p.Z);
        bool safe = SafeZones.Contains(p);
        bool ready = streamer != null && streamer.IsPositionReady(p);
        float yaw = Mathf.RadToDeg(player.Rotation.Y);
        float water = WorldWater.DepthAt(p.X, p.Z);
        var data = new Godot.Collections.Dictionary
        {
            ["region"] = regionId,
            ["cell"] = cell?.Id ?? string.Empty,
            ["in_cell"] = inside,
            ["x"] = R(p.X), ["y"] = R(p.Y), ["z"] = R(p.Z),
            ["yaw"] = R(yaw),
            ["ground"] = R(ground),
            ["safe"] = safe,
            ["ready"] = ready,
            ["water_depth"] = R(Mathf.Max(0f, water)),
        };
        return console.Reply(
            $"{regionId} / {cell?.Id ?? "?"}{(inside ? string.Empty : " (outside its footprint)")}  " +
            $"x {p.X:0.0} y {p.Y:0.0} z {p.Z:0.0}  yaw {yaw:0}  ground {ground:0.0}  " +
            $"{(safe ? "SAFE" : "WILD")}{(ready ? string.Empty : "  NOT-STREAMED")}{(water > 0.2f ? $"  water {water:0.0} m" : string.Empty)}",
            data);
    }

    private static string Tp(DevConsole console, string[] args)
    {
        const string usage = "usage: tp <x> <z> [clearance]|cell <cellId>|loc <locationId>|region <regionId>|out [metres]";
        if (args.Length < 1)
        {
            return console.Fail(usage);
        }

        switch (args[0].ToLowerInvariant())
        {
            case "cell":
            {
                if (args.Length < 2 || RegionDatabase.Cell(args[1]) is not { } cell || RegionOwning(cell.Id) is not { } owner)
                {
                    return console.Fail(args.Length < 2 ? usage : $"unknown cell '{args[1]}'");
                }

                return Teleport(console, owner, cell.Center.X, cell.Center.Z, TeleportClearance, $"cell {cell.Id}");
            }
            case "loc":
            {
                if (args.Length < 2 || MapLocationDatabase.Get(args[1]) is not { } location)
                {
                    return console.Fail(args.Length < 2 ? usage : $"unknown map location '{args[1]}'");
                }

                Vector3? where = TryService(out MapService map) ? map.PositionOf(location.Id) : null;
                where ??= RegionDatabase.Cell(location.CellId)?.Center;
                if (where is not { } point)
                {
                    return console.Fail($"map location '{location.Id}' has no known position");
                }

                RegionResource? region = RegionOwning(location.CellId) ?? RegionAt(point.X, point.Z);
                return region == null
                    ? console.Fail($"map location '{location.Id}' is in no region's cells")
                    : Teleport(console, region, point.X, point.Z, TeleportClearance, $"location {location.Id}");
            }
            case "region":
            {
                if (args.Length < 2 || RegionDatabase.Get(args[1]) is not { } region)
                {
                    return console.Fail(args.Length < 2 ? usage : $"unknown region '{args[1]}'");
                }

                // A region spawn's Y is authored clearance, not a height (WorldSessionDirector.RegionSpawn).
                return Teleport(console, region, region.SpawnPoint.X, region.SpawnPoint.Z,
                    Mathf.Max(region.SpawnPoint.Y, 0.1f), $"region {region.Id}");
            }
            case "out":
                return TpOut(console, args);
            default:
            {
                if (args.Length < 2 || !TryFloat(args, 0, 0f, out float x) || !TryFloat(args, 1, 0f, out float z) ||
                    !TryFloat(args, 2, TeleportClearance, out float clearance) || clearance < 0f || clearance > 500f)
                {
                    return console.Fail(usage);
                }

                return RegionAt(x, z) is { } region
                    ? Teleport(console, region, x, z, clearance, $"{x:0.#}, {z:0.#}")
                    : console.Fail($"{x:0.#}, {z:0.#} is inside no region's cells (see `--state` for cell centres)");
            }
        }
    }

    /// <summary>
    /// Out of the safe zone, deterministically: rings of twelve fixed bearings, widening by 20 m,
    /// take the first point that is inside a cell of the active region, outside every safe zone and
    /// not under water. No randomness, so the same save gives the same landing.
    /// </summary>
    private static string TpOut(DevConsole console, string[] args)
    {
        if (!TryFloat(args, 1, 40f, out float metres) || metres < 5f || metres > 2000f)
        {
            return console.Fail("usage: tp out [metres 5..2000]");
        }

        if (!TryPlayer(out PlayerCharacter player) || !TryService(out RegionStreamer streamer) ||
            RegionDatabase.Get(streamer.ActiveRegionId) is not { } region)
        {
            return console.Fail("no player in a region");
        }

        Vector3 from = player.GlobalPosition;
        for (float radius = metres; radius <= metres + 400f; radius += 20f)
        {
            for (int bearing = 0; bearing < 12; bearing++)
            {
                float angle = bearing * (Mathf.Tau / 12f);
                float x = from.X + (Mathf.Cos(angle) * radius);
                float z = from.Z + (Mathf.Sin(angle) * radius);
                var point = new Vector3(x, 0f, z);
                CellNear(region, point, out bool inside);
                if (inside && !SafeZones.Contains(point) && WorldWater.DepthAt(x, z) <= 0.2f)
                {
                    return Teleport(console, region, x, z, TeleportClearance, $"{radius:0} m out ({x:0.#}, {z:0.#})");
                }
            }
        }

        return console.Fail($"no dry ground outside the safe zones within {metres + 400f:0} m; try `tp cell <id>`");
    }

    /// <summary>
    /// The one teleport. A move into another region goes through the hard load twice: the ground is
    /// one heightfield per region and the destination's only exists once the streamer has been
    /// re-targeted, so the first call swaps the region with the player parked high above the point
    /// and the second, now a same-region move, sets them on the ground that is actually there.
    /// </summary>
    private static string Teleport(DevConsole console, RegionResource region, float x, float z, float clearance, string label)
    {
        if (!TrySession(console, out GameSession session) || session.Players.Player is not { } player ||
            !Node.IsInstanceValid(player) || session.WorldDirector.Streamer == null)
        {
            return console.Fail("no session world to teleport in");
        }

        if (GameManager.Instance?.State is not (GameState.Playing or GameState.Paused))
        {
            return console.Fail($"cannot teleport while the game is {GameManager.Instance?.State}");
        }

        WorldSessionDirector director = session.WorldDirector;
        string message = $"dev: teleporting to {label}";
        bool crossed = region.Id != session.CurrentRegionId;
        if (crossed)
        {
            director.PerformRegionLoad(region, new Vector3(x, 4000f, z), message, autosave: false);
        }

        Vector3 landing = WorldGround.OnGround(new Vector3(x, 0f, z), clearance);
        director.PerformRegionLoad(region, landing, message, autosave: false);
        var data = new Godot.Collections.Dictionary
        {
            ["region"] = region.Id, ["x"] = R(landing.X), ["y"] = R(landing.Y), ["z"] = R(landing.Z),
            ["crossed_region"] = crossed, ["safe"] = SafeZones.Contains(landing),
        };
        return console.Reply(
            $"teleporting to {label}: {region.Id} x {landing.X:0.0} y {landing.Y:0.0} z {landing.Z:0.0} (loading)", data);
    }

    private static string Spawn(DevConsole console, string[] args)
    {
        const string usage = "usage: spawn <templateId> [n] [ahead <m>|at <dx> <dz>] | spawn [n] [templateId] | spawn list [prefix]";
        if (args.Length > 0 && args[0].ToLowerInvariant() == "list")
        {
            string prefix = args.Length > 1 ? args[1] : string.Empty;
            var ids = new List<string>();
            foreach (string id in EnemyTemplateRegistry.TemplateIds)
            {
                if (id.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }

            ids.Sort(System.StringComparer.Ordinal);
            return console.Reply($"{ids.Count} template(s):\n" + string.Join("\n", ids), new Godot.Collections.Array<string>(ids));
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return console.Fail("no player");
        }

        // Template and count in either order: `spawn enemy.wolf 3` or the older `spawn 3 enemy.wolf`.
        string templateId = EnemyTemplateRegistry.FallbackTemplateId;
        int count = 1;
        int next = 0;
        if (args.Length > next && int.TryParse(args[next], out int legacyCount))
        {
            count = legacyCount;
            next++;
            if (args.Length > next && args[next] is not ("ahead" or "at"))
            {
                templateId = args[next++];
            }
        }
        else if (args.Length > next && args[next] is not ("ahead" or "at"))
        {
            templateId = args[next++];
            if (args.Length > next && int.TryParse(args[next], out int n))
            {
                count = n;
                next++;
            }
        }

        if (!EnemyTemplateRegistry.IsRegistered(templateId))
        {
            return console.Fail($"unknown enemy template '{templateId}' (spawn list [prefix])");
        }

        if (count < 1 || count > 50)
        {
            return console.Fail("spawn count must be 1 to 50");
        }

        Vector3 forward = -player.GlobalTransform.Basis.Z;
        forward = new Vector3(forward.X, 0f, forward.Z).Normalized();
        Vector3 anchor = player.GlobalPosition + (forward * 6f);
        if (args.Length > next)
        {
            if (args[next] == "ahead" && args.Length == next + 2 && TryFloat(args, next + 1, 6f, out float ahead))
            {
                anchor = player.GlobalPosition + (forward * ahead);
            }
            else if (args[next] == "at" && args.Length == next + 3 && TryFloat(args, next + 1, 0f, out float dx) &&
                     TryFloat(args, next + 2, 0f, out float dz))
            {
                anchor = player.GlobalPosition + new Vector3(dx, 0f, dz);
            }
            else
            {
                return console.Fail(usage);
            }
        }

        if (!TryService(out RegionStreamer streamer))
        {
            return console.Fail("no region streamer");
        }

        // A fixed ring, not a random scatter: member i of n always stands in the same place.
        float ringRadius = count == 1 ? 0f : Mathf.Min(6f, 0.9f + (0.25f * count));
        var ids2 = new Godot.Collections.Array();
        for (int i = 0; i < count; i++)
        {
            float angle = i * (Mathf.Tau / count);
            Vector3 desired = WorldGround.OnGround(
                anchor + new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius), 0.5f);

            // The same two refusals the encounter director applies: no validated ground, no spawn;
            // no active cell to own the actor, no spawn.
            if (!SpawnPlacement.TryResolve(player, desired, out Vector3 position))
            {
                continue;
            }

            EnemyEntity enemy = EnemyTemplateRegistry.Create(templateId, position);
            if (!streamer.TryAddCellOwnedActor(enemy, position))
            {
                enemy.Free();
                continue;
            }

            ids2.Add(enemy.RuntimeId);
        }

        var data = new Godot.Collections.Dictionary
        {
            ["template"] = templateId, ["requested"] = count, ["spawned"] = ids2.Count, ["ids"] = ids2,
            ["x"] = R(anchor.X), ["z"] = R(anchor.Z), ["safe"] = SafeZones.Contains(anchor),
        };
        string reply = $"spawned {ids2.Count}/{count} x {templateId} around {anchor.X:0.0}, {anchor.Z:0.0}";
        return ids2.Count == 0
            ? console.Fail(reply + " (no validated ground or no active cell there)")
            : console.Reply(reply, data);
    }

    private static string Face(DevConsole console, string[] args)
    {
        const string usage = "usage: face <yawDegrees> [pitchDegrees]|nearest|at <x> <z>";
        if (!TryPlayer(out PlayerCharacter player))
        {
            return console.Fail("no player");
        }

        if (args.Length < 1)
        {
            return console.Fail(usage);
        }

        float yaw;
        float pitch = 0f;
        Vector3? target = null;
        if (args[0] == "nearest")
        {
            List<EnemyEntity> enemies = LiveEnemies(player, float.MaxValue);
            if (enemies.Count == 0)
            {
                return console.Fail("no live enemy to face");
            }

            target = enemies[0].GlobalPosition + new Vector3(0f, 1f, 0f);
        }
        else if (args[0] == "at")
        {
            if (args.Length != 3 || !TryFloat(args, 1, 0f, out float x) || !TryFloat(args, 2, 0f, out float z))
            {
                return console.Fail(usage);
            }

            target = new Vector3(x, player.GlobalPosition.Y + 1.6f, z);
        }

        if (target is { } point)
        {
            Vector3 to = point - (player.GlobalPosition + new Vector3(0f, 1.6f, 0f));
            var flat = new Vector3(to.X, 0f, to.Z);
            if (flat.LengthSquared() < 0.0001f)
            {
                return console.Fail("the target is where the player stands");
            }

            yaw = Mathf.RadToDeg(Mathf.Atan2(-flat.X, -flat.Z));
            pitch = Mathf.RadToDeg(Mathf.Atan2(to.Y, flat.Length()));
        }
        else if (!TryFloat(args, 0, 0f, out yaw) || !TryFloat(args, 1, 0f, out pitch) || args.Length > 2)
        {
            return console.Fail(usage);
        }

        pitch = Mathf.Clamp(pitch, -89f, 89f);
        player.Rotation = new Vector3(0f, Mathf.DegToRad(yaw), 0f);

        // The rig subtracts a step from its pitch and clamps to its own limit (PlayerLookInput's path).
        if (player.GetComponent<PlayerCameraRig>() is { CameraPivot: { } pivot } rig)
        {
            rig.ApplyPitchStep(pivot.Rotation.X - Mathf.DegToRad(pitch), invertY: false);
        }

        return console.Reply($"facing yaw {yaw:0.#} pitch {pitch:0.#}",
            new Godot.Collections.Dictionary { ["yaw"] = R(yaw), ["pitch"] = R(pitch) });
    }

    // --- Cells --------------------------------------------------------------

    /// <summary>The cell of <paramref name="region"/> whose footprint holds the point, else the
    /// nearest one. The streamer's own lookup is private; this is the same rule.</summary>
    private static RegionCellResource? CellNear(RegionResource? region, Vector3 point, out bool inside)
    {
        inside = false;
        RegionCellResource? nearest = null;
        float nearestDistance = float.MaxValue;
        if (region == null)
        {
            return null;
        }

        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell == null)
            {
                continue;
            }

            Vector2 half = cell.Presentation == null
                ? new Vector2(30f, 30f)
                : new Vector2(cell.Presentation.Width * 0.5f, cell.Presentation.Depth * 0.5f);
            float distance = WorldStreamingPolicy.DistanceToFootprint(point, cell.Center, half);
            if (distance <= 0.01f)
            {
                inside = true;
                return cell;
            }

            if (distance < nearestDistance)
            {
                nearest = cell;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static RegionResource? RegionAt(float x, float z)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            CellNear(region, new Vector3(x, 0f, z), out bool inside);
            if (inside)
            {
                return region;
            }
        }

        return null;
    }

    private static RegionResource? RegionOwning(string cellId)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == cellId)
                {
                    return region;
                }
            }
        }

        return null;
    }
}
