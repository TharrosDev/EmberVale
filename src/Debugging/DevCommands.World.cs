using System.Collections.Generic;
using System.Globalization;
using Embervale.Bootstrap;
using Embervale.Core.Diagnostics;
using Embervale.Items;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The world's dials and the player's ledgers: <c>time</c>, <c>timescale</c>, <c>weather</c>,
/// <c>event</c>, <c>quest list|status</c>, <c>inv</c>, <c>take</c>, <c>save</c>, <c>load</c>,
/// <c>log</c>. Each goes through the call the game itself uses (the clock's setter, the directors'
/// force methods, the inventory, the session's save and reload).
/// </summary>
public static partial class DevCommands
{
    /// <summary>The slot <c>save</c> and <c>load</c> use when none is named.</summary>
    private const string ConsoleSlot = "console";

    /// <summary>Set for a script run that was allowed onto the real save folder
    /// (<c>--exec-allow-real-save</c>): <c>save</c> then writes <see cref="ConsoleSlot"/> only.</summary>
    public static bool SaveConsoleSlotOnly { get; set; }

    private static void RegisterWorld(DevConsole console)
    {
        console.Register(new ConsoleCommand("time", "time [<hour>|+<hours>|day <n>]", "Show the clock, set the hour (24 or more rolls the date), advance by hours, or advance to the same hour on day n.", Time));
        console.Register(new ConsoleCommand("timescale", "timescale [<0.05..20>]", "Show or set the engine time scale. Physics steps per frame change with it, so a fight can play out differently.", TimeScale));
        console.Register(new ConsoleCommand("weather", "weather [list|<id>|stop]", "Show the weather, list the states, force one, or return to the region's default.", Weather));
        console.Register(new ConsoleCommand("event", "event [list|<id>|stop]", "Show the active world event, list them, force one to start, or end the active one as a failure.", Event));
        console.Register(new ConsoleCommand("inv", "inv [prefix]", "List what the player carries (pack and material bag), optionally only ids starting with a prefix.", Inv));
        console.Register(new ConsoleCommand("take", "take <itemId> [qty]", "Remove items from the player through the inventory.", Take));
        console.Register(new ConsoleCommand("save", "save [slot]", "Save through the session's own save path (default slot 'console'). Refused where the game refuses a save.", Save));
        console.Register(new ConsoleCommand("load", "load [slot]", "Reload a slot through the session's reload path (default 'console'). The session is rebuilt; the reply comes back before it is.", Load));
        console.Register(new ConsoleCommand("log", "log [trace|info|warn|error]", "Show or set the minimum log level.", LogLevel));
    }

    private static string Time(DevConsole console, string[] args)
    {
        const string usage = "usage: time [<hour>|+<hours>|day <n>]";
        if (!TryService(out WorldClock clock))
        {
            return console.Fail("no clock");
        }

        if (args.Length > 0)
        {
            string first = args[0];
            if (first.Equals("day", System.StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day))
                {
                    return console.Fail(usage);
                }

                if (day <= clock.Day)
                {
                    return console.Fail($"day {day} is not after day {clock.Day}: the clock only runs forward");
                }

                clock.SetTimeOfDay(clock.TimeOfDay + (24f * (day - clock.Day)));
            }
            else if (!float.TryParse(first.TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out float hours) ||
                     !float.IsFinite(hours) || hours < 0f || hours > 24f * 400f || args.Length > 1)
            {
                return console.Fail(usage);
            }
            else
            {
                clock.SetTimeOfDay(first.StartsWith('+') ? clock.TimeOfDay + hours : hours);
            }
        }

        var data = new Godot.Collections.Dictionary
        {
            ["hour"] = R(clock.TimeOfDay), ["day"] = clock.Day, ["phase"] = clock.Phase.ToString(),
        };
        return console.Reply($"{(args.Length > 0 ? "time set to" : "time")} {clock.Clock()} day {clock.Day} ({clock.Phase})", data);
    }

    private static string TimeScale(DevConsole console, string[] args)
    {
        if (args.Length > 0)
        {
            if (!TryFloat(args, 0, 1f, out float scale) || scale < 0.05f || scale > 20f)
            {
                return console.Fail("usage: timescale [<0.05..20>]");
            }

            Engine.TimeScale = scale;
        }

        return console.Reply($"timescale {Engine.TimeScale:0.##}",
            new Godot.Collections.Dictionary { ["timescale"] = System.Math.Round(Engine.TimeScale, 3) });
    }

    private static string Weather(DevConsole console, string[] args)
    {
        if (!TryService(out WeatherDirector weather))
        {
            return console.Fail("no weather director");
        }

        string verb = args.Length > 0 ? args[0] : string.Empty;
        if (verb == "list")
        {
            var ids = new Godot.Collections.Array();
            var lines = new List<string>();
            foreach (WeatherResource state in WeatherDatabase.All)
            {
                ids.Add(state.Id);
                lines.Add($"{state.Id}  — {state.DisplayName}{(state.Id == weather.Current?.Id ? "  (current)" : string.Empty)}");
            }

            return console.Reply(string.Join("\n", lines), ids);
        }

        if (verb == "stop")
        {
            string fallback = TryService(out RegionStreamer streamer) && RegionDatabase.Get(streamer.ActiveRegionId) is { } region
                ? region.DefaultWeatherId
                : "weather.clear";
            if (!weather.Force(fallback))
            {
                return console.Fail($"the region's default weather '{fallback}' is not authored");
            }
        }
        else if (verb.Length > 0 && !weather.Force(verb))
        {
            return console.Fail($"unknown weather '{verb}' (weather list)");
        }

        string current = weather.Current?.Id ?? "none";
        return console.Reply($"weather {(verb.Length > 0 ? "→ " : string.Empty)}{current}",
            new Godot.Collections.Dictionary { ["weather"] = current });
    }

    private static string Event(DevConsole console, string[] args)
    {
        if (!TryService(out WorldEventDirector director))
        {
            return console.Fail("no world-event director");
        }

        string verb = args.Length > 0 ? args[0] : string.Empty;
        if (verb == "list")
        {
            var ids = new Godot.Collections.Array();
            var lines = new List<string>();
            foreach (WorldEventResource resource in WorldEventDatabase.All)
            {
                ids.Add(resource.Id);
                lines.Add(resource.Id + (resource.Id == director.Active?.Resource.Id ? "  (active)" : string.Empty));
            }

            return console.Reply(string.Join("\n", lines), ids);
        }

        if (verb == "stop")
        {
            if (!director.ForceStop())
            {
                return console.Fail("no world event is active");
            }
        }
        else if (verb.Length > 0)
        {
            if (WorldEventDatabase.Get(verb) == null)
            {
                return console.Fail($"unknown world event '{verb}' (event list)");
            }

            if (!director.ForceStart(verb))
            {
                return console.Fail(director.Active is { } running
                    ? $"could not start '{verb}': '{running.Resource.Id}' is active (event stop)"
                    : $"could not start '{verb}': it found nowhere to materialise");
            }
        }

        WorldEvent? active = director.Active;
        var data = new Godot.Collections.Dictionary
        {
            ["event"] = active?.Resource.Id ?? "none",
            ["progress"] = active?.Progress ?? 0,
            ["required"] = active?.Required ?? 0,
            ["seconds_left"] = active is { IsTimed: true } ? System.Math.Round(active.TimeLeft, 1) : -1d,
        };
        string text = active == null
            ? "event none"
            : $"event {active.Resource.Id}  {active.Progress}/{active.Required}" +
              (active.IsTimed ? $"  {active.TimeLeft:0}s left" : string.Empty);
        return console.Reply(verb == "stop" ? "event stopped" : text, data);
    }

    /// <summary><c>quest list [active|done|failed]</c> and <c>quest status &lt;id&gt;</c>: the log as
    /// it stands, objectives with their counts, inert branches marked.</summary>
    private static string QuestReport(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<QuestLogComponent>() is not { } log)
        {
            return console.Fail("no player quest log");
        }

        if (args[0].ToLowerInvariant() == "status")
        {
            if (args.Length < 2 || QuestDatabase.Get(args[1]) is not { } quest)
            {
                return console.Fail(args.Length < 2 ? "usage: quest status <questId>" : $"unknown quest '{args[1]}'");
            }

            foreach (QuestProgress progress in log.Quests)
            {
                if (progress.Quest.Id == quest.Id)
                {
                    Godot.Collections.Dictionary row = QuestRow(progress, objectives: true);
                    var lines = new List<string> { $"{quest.Id}  {progress.Status}" };
                    foreach (Variant objective in row["objectives"].AsGodotArray())
                    {
                        Godot.Collections.Dictionary o = objective.AsGodotDictionary();
                        lines.Add($"  [{o["i"]}] {o["label"]} {o["count"]}/{o["required"]}{(o["active"].AsBool() ? string.Empty : " [inert]")}");
                    }

                    return console.Reply(string.Join("\n", lines), row);
                }
            }

            return console.Reply($"{quest.Id}  not in the log (can start: {log.CanStart(quest)})",
                new Godot.Collections.Dictionary { ["id"] = quest.Id, ["status"] = "none", ["can_start"] = log.CanStart(quest) });
        }

        string filter = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
        QuestStatus? wanted = filter switch
        {
            "active" => QuestStatus.Active,
            "done" => QuestStatus.Completed,
            "failed" => QuestStatus.Failed,
            _ => null,
        };
        if (wanted == null && filter != "all")
        {
            return console.Fail("usage: quest list [active|done|failed]");
        }

        var rows = new Godot.Collections.Array();
        var text = new List<string>();
        foreach (QuestProgress progress in log.Quests)
        {
            if (wanted == null || progress.Status == wanted)
            {
                rows.Add(QuestRow(progress, objectives: false));
                text.Add($"{progress.Quest.Id}  {progress.Status}");
            }
        }

        text.Sort(System.StringComparer.Ordinal);
        return console.Reply($"{rows.Count} quest(s){(text.Count > 0 ? "\n" : string.Empty)}{string.Join("\n", text)}", rows);
    }

    private static Godot.Collections.Dictionary QuestRow(QuestProgress progress, bool objectives)
    {
        var row = new Godot.Collections.Dictionary
        {
            ["id"] = progress.Quest.Id, ["status"] = progress.Status.ToString(),
        };
        if (!objectives)
        {
            return row;
        }

        var list = new Godot.Collections.Array();
        var authored = progress.Quest.ObjectiveList();
        for (int i = 0; i < authored.Count; i++)
        {
            list.Add(new Godot.Collections.Dictionary
            {
                ["i"] = i, ["label"] = authored[i].ShortLabel(), ["count"] = progress.Counts[i],
                ["required"] = authored[i].RequiredCount, ["active"] = progress.IsObjectiveActive(i),
            });
        }

        row["objectives"] = list;
        return row;
    }

    private static string Inv(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<InventoryComponent>() is not { } inventory)
        {
            return console.Fail("no player inventory");
        }

        string prefix = args.Length > 0 ? args[0] : string.Empty;
        var totals = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        foreach (IReadOnlyList<ItemStack> source in new[] { inventory.Stacks, inventory.Materials })
        {
            foreach (ItemStack stack in source)
            {
                string id = stack.Item.Id;
                if (id.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    totals[id] = totals.GetValueOrDefault(id) + stack.Quantity;
                }
            }
        }

        var data = new Godot.Collections.Dictionary();
        var lines = new List<string> { $"{totals.Count} item id(s), {inventory.UsedSlots}/{inventory.UsedSlots + inventory.FreeSlots} pack slots" };
        foreach (KeyValuePair<string, int> pair in totals)
        {
            data[pair.Key] = pair.Value;
            lines.Add($"  {pair.Key} x{pair.Value}");
        }

        return console.Reply(string.Join("\n", lines), data);
    }

    private static string Take(DevConsole console, string[] args)
    {
        if (args.Length < 1 || !TryInt(args, 1, 1, out int quantity) || quantity < 1)
        {
            return console.Fail("usage: take <itemId> [qty]");
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<InventoryComponent>() is not { } inventory)
        {
            return console.Fail("no player inventory");
        }

        if (ItemDatabase.Get(args[0]) == null)
        {
            return console.Fail($"unknown item '{args[0]}'");
        }

        int held = inventory.CountOf(args[0]);
        return inventory.RemoveItem(args[0], quantity)
            ? $"took {quantity}x {args[0]} ({held - quantity} left)"
            : console.Fail($"cannot take {quantity}x {args[0]}: the player holds {held}");
    }

    private static string Save(DevConsole console, string[] args)
    {
        if (!TrySession(console, out GameSession session))
        {
            return console.Fail("no session to save");
        }

        string slot = args.Length > 0 ? args[0] : ConsoleSlot;
        if (SaveConsoleSlotOnly && slot != ConsoleSlot)
        {
            return console.Fail($"save to '{slot}' refused: this run is on the real save folder, so only the " +
                                $"'{ConsoleSlot}' slot may be written (isolate it with EMBERVALE_USER_DIR)");
        }

        return session.Lifecycle.TrySave(slot, out string failureKey)
            ? console.Reply($"saved to '{slot}'", new Godot.Collections.Dictionary { ["slot"] = slot })
            : console.Fail($"save to '{slot}' refused ({(failureKey.Length > 0 ? failureKey : "not a player-writable slot")})");
    }

    private static string Load(DevConsole console, string[] args)
    {
        if (!TrySession(console, out GameSession session))
        {
            return console.Fail("no session to reload");
        }

        string slot = args.Length > 0 ? args[0] : ConsoleSlot;
        if (SaveManager.Instance is not { } saves || !saves.SaveExists(slot))
        {
            return console.Fail($"no save in slot '{slot}'");
        }

        return session.Lifecycle.RequestReload(session, slot)
            ? console.Reply($"reloading '{slot}' (the session is rebuilt next frame)", new Godot.Collections.Dictionary { ["slot"] = slot })
            : console.Fail($"reload of '{slot}' refused (not playing, a reload is already pending, or the slot is unreadable)");
    }

    private static string LogLevel(DevConsole console, string[] args)
    {
        if (args.Length > 0)
        {
            if (!System.Enum.TryParse(args[0], ignoreCase: true, out Log.Level level) || !System.Enum.IsDefined(level))
            {
                return console.Fail("usage: log [trace|info|warn|error]");
            }

            Log.MinimumLevel = level;
        }

        return $"log level {Log.MinimumLevel.ToString().ToLowerInvariant()}";
    }
}
