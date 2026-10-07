using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Save;
using Embervale.Stats;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Reading state: <c>get</c> (one value per key, the thing <c>assert</c> and <c>wait-until</c>
/// test), <c>dump</c> (a JSON document) and <c>hud</c> (the F3 overlay as text).
///
/// <para>⚠️ A new key goes in <see cref="QueryKeys"/> and in <see cref="Query"/> together:
/// <c>get</c> with no argument prints the first, and a key missing from it is a key nobody finds.</para>
/// </summary>
public static partial class DevCommands
{
    /// <summary>A dump no longer than this is replied inline; a longer one is written to a file
    /// and the reply is its path.</summary>
    private const int InlineDumpCharacters = 600;

    /// <summary>Every key <c>get</c> answers. A trailing <c>&lt;…&gt;</c> is a family: the id
    /// follows the dot.</summary>
    public static readonly string[] QueryKeys =
    {
        "state", "frame", "fps", "timescale", "invariants", "orphans", "nodes", "menu",
        "region", "cells.active", "cells.resident", "world.settled", "world.ready",
        "time.hour", "time.day", "time.phase", "weather", "event",
        "enemies.count", "enemies.near",
        "player.hp", "player.hp_max", "player.mana", "player.stamina", "player.alive", "player.god",
        "player.level", "player.gold", "player.corruption", "player.mounted",
        "player.x", "player.y", "player.z", "player.yaw", "player.cell", "player.safe",
        "flag.<flagId>", "quest.<questId>", "item.<itemId>", "spell.<spellId>", "rep.<factionId>", "stat.<StatType>",
    };

    private static void RegisterQuery(DevConsole console)
    {
        console.Register(new ConsoleCommand("get", "get [<key>...]", "Print key=value for each state key, or list the keys. These are the keys the script runner's assert and wait-until test.", Get));
        console.Register(new ConsoleCommand("dump", "dump <player|enemies|entity <runtimeId>|world|saveables> [fileName]", "State as JSON: an entity with every saveable component's own Save(), the live enemies, the world's dials, or the registered save ids. Replied inline when short, else written under the console output directory.", Dump));
        console.Register(new ConsoleCommand("skip", "skip", "End the narration that is playing (the New Game prologue, a vision, an ending card) the way holding the skip key does. The script runner does this once before its first statement.", Skip));
        console.Register(new ConsoleCommand("hud", "hud [on|off]", "Print the F3 debug HUD as text, or show or hide the overlay.", Hud));
    }

    /// <summary>Where <c>dump</c> files, script results and <c>shot</c> images go:
    /// <c>$EMBERVALE_ARTIFACTS/console</c>, else <c>$EMBERVALE_USER_DIR/console</c> when that is an
    /// absolute path (an isolated run must not write into the player's folder), else
    /// <c>user://console</c>. Created on demand.</summary>
    public static string OutputDirectory()
    {
        string root = OS.GetEnvironment("EMBERVALE_ARTIFACTS");
        if (string.IsNullOrEmpty(root))
        {
            string userDir = OS.GetEnvironment("EMBERVALE_USER_DIR");
            root = System.IO.Path.IsPathFullyQualified(userDir) ? userDir : ProjectSettings.GlobalizePath("user://");
        }

        string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "console"));
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>The value of one <c>get</c> key as text (<see cref="ConsoleText.Format"/>). False
    /// when the key is unknown or what it reads does not exist right now (no player, no clock).</summary>
    public static bool TryQuery(string key, out string value)
    {
        object? raw = Query(key);
        value = ConsoleText.Format(raw);
        return raw != null;
    }

    private static string Get(DevConsole console, string[] args)
    {
        if (args.Length == 0)
        {
            return "keys: " + string.Join(" ", QueryKeys);
        }

        var data = new Godot.Collections.Dictionary();
        var lines = new List<string>();
        var unknown = new List<string>();
        foreach (string key in args)
        {
            if (TryQuery(key, out string value))
            {
                // Typed in the JSON reply: a boolean, a number, or the text.
                data[key] = value is "true" or "false"
                    ? Variant.From(value == "true")
                    : ConsoleText.TryNumber(value, out double number) ? Variant.From(number) : Variant.From(value);
                lines.Add($"{key}={value}");
            }
            else
            {
                unknown.Add(key);
                lines.Add($"{key}=?");
            }
        }

        string text = string.Join("\n", lines);
        return unknown.Count > 0
            ? console.Fail(text + $"\nunknown or unavailable: {string.Join(" ", unknown)} (`get` lists the keys)")
            : console.Reply(text, data);
    }

    private static object? Query(string key)
    {
        switch (key)
        {
            case "state": return GameManager.Instance?.State.ToString();
            case "frame": return (long)Engine.GetProcessFrames();
            case "fps": return Engine.GetFramesPerSecond();
            case "timescale": return Engine.TimeScale;
            case "invariants": return Invariant.Violations;
            case "orphans": return (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            case "nodes": return (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            case "menu": return UiState.MenuOpen;
        }

        if (key.StartsWith("time.", System.StringComparison.Ordinal))
        {
            if (!TryService(out WorldClock clock))
            {
                return null;
            }

            return key switch
            {
                "time.hour" => (object?)clock.TimeOfDay,
                "time.day" => clock.Day,
                "time.phase" => clock.Phase.ToString(),
                _ => null,
            };
        }

        if (key == "weather")
        {
            return TryService(out WeatherDirector weather) ? weather.Current?.Id ?? "none" : null;
        }

        if (key == "event")
        {
            return TryService(out WorldEventDirector events) ? events.Active?.Resource.Id ?? "none" : null;
        }

        bool hasPlayer = TryPlayer(out PlayerCharacter player);
        if (key is "region" or "cells.active" or "cells.resident" or "world.settled" or "world.ready")
        {
            if (!TryService(out RegionStreamer streamer))
            {
                return null;
            }

            return key switch
            {
                "region" => (object?)streamer.ActiveRegionId,
                "cells.active" => streamer.ActiveCellCount(),
                "cells.resident" => streamer.ResidentCellCount(),
                "world.settled" => streamer.IsSettled(),
                _ => hasPlayer && streamer.IsPositionReady(player.GlobalPosition),
            };
        }

        if (!hasPlayer)
        {
            return null;
        }

        switch (key)
        {
            case "enemies.count": return LiveEnemies(player, float.MaxValue).Count;
            case "enemies.near": return LiveEnemies(player, 30f).Count;
            case "player.x": return player.GlobalPosition.X;
            case "player.y": return player.GlobalPosition.Y;
            case "player.z": return player.GlobalPosition.Z;
            case "player.yaw": return Mathf.RadToDeg(player.Rotation.Y);
            case "player.safe": return SafeZones.Contains(player.GlobalPosition);
            case "player.cell":
                return TryService(out RegionStreamer where)
                    ? CellNear(RegionDatabase.Get(where.ActiveRegionId), player.GlobalPosition, out _)?.Id ?? "none"
                    : null;
            case "player.level": return player.GetComponent<ProgressionComponent>()?.Level;
            case "player.gold": return player.GetComponent<InventoryComponent>()?.CountOf(GameIds.Currency.Gold);
            case "player.corruption": return player.GetComponent<CorruptionComponent>()?.Value;
            case "player.mounted": return player.GetComponent<MountComponent>()?.IsMounted ?? false;
        }

        StatsComponent? stats = player.GetComponent<StatsComponent>();
        switch (key)
        {
            case "player.hp": return stats?.GetCurrent(StatType.Health);
            case "player.hp_max": return stats?.GetMax(StatType.Health);
            case "player.mana": return stats?.GetCurrent(StatType.Mana);
            case "player.stamina": return stats?.GetCurrent(StatType.Stamina);
            case "player.alive": return stats?.IsAlive;
            case "player.god": return stats == null ? null : IsGod(stats);
        }

        int dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
        {
            return null;
        }

        string id = key[(dot + 1)..];
        switch (key[..dot])
        {
            case "flag": return player.GetComponent<StoryFlagsComponent>()?.Has(id);
            case "item": return player.GetComponent<InventoryComponent>()?.CountOf(id);
            case "rep": return player.GetComponent<ReputationComponent>()?.Get(id);
            case "stat":
                return stats != null && System.Enum.TryParse(id, ignoreCase: true, out StatType type) && System.Enum.IsDefined(type)
                    ? stats.GetValue(type)
                    : null;
            case "spell":
            {
                if (player.GetComponent<SpellcastingComponent>() is not { } casting)
                {
                    return null;
                }

                foreach (SpellResource spell in casting.Spells)
                {
                    if (spell.Id == id)
                    {
                        return true;
                    }
                }

                return false;
            }
            case "quest":
            {
                if (player.GetComponent<QuestLogComponent>() is not { } log)
                {
                    return null;
                }

                foreach (QuestProgress progress in log.Quests)
                {
                    if (progress.Quest.Id == id)
                    {
                        return progress.Status.ToString();
                    }
                }

                return "none";
            }
            default:
                return null;
        }
    }

    private static string Dump(DevConsole console, string[] args)
    {
        const string usage = "usage: dump <player|enemies|entity <runtimeId>|world|saveables> [fileName]";
        if (args.Length < 1)
        {
            return console.Fail(usage);
        }

        string what = args[0].ToLowerInvariant();
        int fileArgument = what == "entity" ? 2 : 1;
        string? fileName = args.Length > fileArgument ? args[fileArgument] : null;
        if (args.Length > fileArgument + 1 || (fileName != null && !ConsoleText.IsFileName(fileName)))
        {
            return console.Fail(usage + " (the file name is letters, digits, _ and -)");
        }

        bool hasPlayer = TryPlayer(out PlayerCharacter player);
        Variant data;
        switch (what)
        {
            case "player" when hasPlayer:
                data = EntityDump(player);
                break;
            case "enemies" when hasPlayer:
                data = EnemyRows(player, LiveEnemies(player, float.MaxValue));
                break;
            case "entity" when hasPlayer:
            {
                if (args.Length < 2 || !ulong.TryParse(args[1], out ulong runtimeId))
                {
                    return console.Fail(usage);
                }

                if (FindEntity(player.GetTree()?.Root, runtimeId) is not { } entity)
                {
                    return console.Fail($"no entity with runtime id {runtimeId} (enemies lists ids)");
                }

                data = EntityDump(entity);
                break;
            }
            case "world":
                data = WorldDump(hasPlayer ? player : null);
                break;
            case "saveables":
            {
                if (SaveManager.Instance is not { } saves)
                {
                    return console.Fail("save manager unavailable");
                }

                var ids = new List<string>(saves.RegisteredSaveIds);
                ids.Sort(System.StringComparer.Ordinal);
                data = new Godot.Collections.Dictionary
                {
                    ["count"] = ids.Count, ["ids"] = new Godot.Collections.Array<string>(ids),
                };
                break;
            }
            case "player" or "enemies" or "entity":
                return console.Fail("no player");
            default:
                return console.Fail(usage);
        }

        string json = Json.Stringify(data);
        if (fileName == null && json.Length <= InlineDumpCharacters)
        {
            return console.Reply(json, json);
        }

        string path = System.IO.Path.Combine(
            OutputDirectory(), $"{fileName ?? $"dump-{what}-{Engine.GetProcessFrames()}"}.json");
        System.IO.File.WriteAllText(path, json);
        return console.Reply($"dump {what}: {path} ({json.Length} chars)",
            new Godot.Collections.Dictionary { ["path"] = path, ["chars"] = json.Length });
    }

    /// <summary>One actor as data. The component states are whatever each saveable component's own
    /// <c>Save()</c> returns, so this shows exactly what a save would hold for it.</summary>
    private static Godot.Collections.Dictionary EntityDump(IEntity entity)
    {
        Vector3 p = entity.Body.GlobalPosition;
        var dump = new Godot.Collections.Dictionary
        {
            ["id"] = entity.RuntimeId,
            ["template"] = entity.TemplateId,
            ["name"] = entity.DisplayName,
            ["persistent_id"] = entity.PersistentId ?? string.Empty,
            ["x"] = R(p.X), ["y"] = R(p.Y), ["z"] = R(p.Z),
            ["yaw"] = R(Mathf.RadToDeg(entity.Body.Rotation.Y)),
        };

        if (entity.GetComponent<StatsComponent>() is { } stats)
        {
            var values = new Godot.Collections.Dictionary { ["alive"] = stats.IsAlive };
            foreach (StatType resource in new[] { StatType.Health, StatType.Stamina, StatType.Mana })
            {
                values[resource.ToString().ToLowerInvariant()] = R(stats.GetCurrent(resource));
                values[resource.ToString().ToLowerInvariant() + "_max"] = R(stats.GetMax(resource));
            }

            foreach (StatType stat in new[] { StatType.PhysicalPower, StatType.SpellPower, StatType.Armor })
            {
                values[stat.ToString()] = R(stats.GetValue(stat));
            }

            dump["stats"] = values;
        }

        if (entity.GetComponent<EnemyAIComponent>() is { } ai)
        {
            dump["ai_state"] = ai.State.ToString();
        }

        var names = new Godot.Collections.Array();
        var saved = new Godot.Collections.Dictionary();
        foreach (EntityComponent component in entity.GetComponents<EntityComponent>())
        {
            names.Add(component.GetType().Name);
            if (component is ISaveable saveable)
            {
                try
                {
                    saved[saveable.SaveId] = saveable.Save();
                }
                catch (System.Exception error)
                {
                    saved[saveable.SaveId] = $"Save() threw: {error.Message}";
                }
            }
        }

        dump["components"] = names;
        dump["saved"] = saved;
        return dump;
    }

    private static Godot.Collections.Dictionary WorldDump(PlayerCharacter? player)
    {
        var dump = new Godot.Collections.Dictionary();
        foreach (string key in new[]
                 {
                     "state", "region", "cells.active", "cells.resident", "world.settled", "world.ready",
                     "time.hour", "time.day", "time.phase", "weather", "event", "timescale", "invariants",
                     "orphans", "nodes", "enemies.count", "enemies.near", "player.cell", "player.safe",
                 })
        {
            if (TryQuery(key, out string value))
            {
                dump[key] = value;
            }
        }

        if (player != null)
        {
            Vector3 p = player.GlobalPosition;
            dump["player"] = new Godot.Collections.Array { R(p.X), R(p.Y), R(p.Z) };
        }

        return dump;
    }

    private static IEntity? FindEntity(Node? node, ulong runtimeId)
    {
        if (node == null)
        {
            return null;
        }

        if (node is IEntity entity && entity.RuntimeId == runtimeId)
        {
            return entity;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindEntity(child, runtimeId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Ends every narration sequence playing under <paramref name="root"/> through its own
    /// skip path. Returns their type names, empty when none was playing.</summary>
    public static List<string> SkipNarration(Node root)
    {
        var skipped = new List<string>();
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (node is NarrationSequence { IsPlaying: true } sequence)
            {
                skipped.Add(sequence.GetType().Name);
                sequence.SkipNow();
            }

            foreach (Node child in node.GetChildren())
            {
                pending.Push(child);
            }
        }

        return skipped;
    }

    private static string Skip(DevConsole console, string[] args)
    {
        List<string> skipped = SkipNarration(console.GetTree().Root);
        return skipped.Count == 0 ? "nothing to skip: narration is not playing" : "skipped " + string.Join(", ", skipped);
    }

    private static string Hud(DevConsole console, string[] args)
    {
        if (console.GetParent()?.GetNodeOrNull<DebugHud>("DebugHud") is not { } hud)
        {
            return console.Fail("no debug HUD in this session");
        }

        if (args.Length == 0)
        {
            return hud.Snapshot();
        }

        string verb = args[0].ToLowerInvariant();
        if (verb is not ("on" or "off"))
        {
            return console.Fail("usage: hud [on|off]");
        }

        hud.SetShown(verb == "on");
        return $"hud {verb}";
    }
}
