using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// A tiny reproduction harness: named scenarios that seed the global RNG to a fixed value and
/// then run a fixed sequence of dev-console commands, so a bug can be reproduced deterministically
/// with <c>repro &lt;name&gt;</c>. The seed makes the otherwise-random world (encounter rolls,
/// patrol points, loot) repeatable; new scenarios are a one-line entry here.
///
/// (Note: <see cref="Embervale.Loot.LootGenerator"/> keeps its own RNG; seed it separately when a
/// loot-specific repro needs determinism.)
/// </summary>
public static class ReproHarness
{
    private sealed record Scenario(ulong Seed, string[] Commands);

    private static readonly Dictionary<string, Scenario> Scenarios = new(StringComparer.OrdinalIgnoreCase)
    {
        ["swarm"] = new Scenario(1uL, new[] { "spawn 6" }),
        ["rich"] = new Scenario(2uL, new[] { "give item.currency.gold 500", "give item.weapon.steel_sword 1", "xp 300" }),
        ["duskstorm"] = new Scenario(3uL, new[] { "time 19", "weather weather.storm" }),
        ["raid"] = new Scenario(4uL, new[] { "event event.goblin_raid" }),
    };

    public static IEnumerable<string> Names => Scenarios.Keys;

    /// <summary>Runs a scenario by name, executing each step through <paramref name="exec"/>
    /// (the console's command runner) after seeding the RNG. Returns a transcript.</summary>
    public static string Run(string name, Func<string, string> exec)
    {
        if (!Scenarios.TryGetValue(name, out Scenario? scenario))
        {
            return $"unknown scenario '{name}' — try: {string.Join(", ", Scenarios.Keys)}";
        }

        GD.Seed(scenario.Seed);

        var sb = new StringBuilder($"repro '{name}' (seed {scenario.Seed}):\n");
        foreach (string command in scenario.Commands)
        {
            string output = exec(command);
            sb.Append($"  {command} → {output}\n");
        }

        return sb.ToString().TrimEnd();
    }
}
