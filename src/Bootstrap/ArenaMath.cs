using System;
using System.Collections.Generic;
using System.Globalization;

namespace Embervale.Bootstrap;

/// <summary>How the arena's bot plays the player.</summary>
public enum ArenaPolicy
{
    /// <summary>Close the distance and swing whenever something is in reach.</summary>
    Aggressive,

    /// <summary>As aggressive, but raise the guard while the target is winding up a blow.</summary>
    Guard,

    /// <summary>Stand still and do nothing: measures what the enemy deals to an idle player.</summary>
    Passive,
}

/// <summary>What the bot knows this tick.</summary>
/// <param name="Gap">Metres between the two bodies' surfaces (negative when overlapping).</param>
/// <param name="TargetWindingUp">The target announced a blow that has not finished yet.</param>
/// <param name="StaminaFraction">The player's stamina, 0..1.</param>
public readonly record struct ArenaSense(float Gap, bool TargetWindingUp, float StaminaFraction);

/// <summary>What the bot holds down this tick.</summary>
public readonly record struct ArenaIntent(bool Forward, bool Attack, bool Block);

/// <summary>One fight, as counted from the game's own combat events.</summary>
/// <param name="Outcome"><c>win</c> (every enemy dead), <c>loss</c> (the player died) or
/// <c>timeout</c>.</param>
/// <param name="Seconds">Game seconds from the spawn to the outcome.</param>
/// <param name="Dealt">Damage the player dealt to the arena's enemies.</param>
/// <param name="Taken">Damage the player took, from anything.</param>
/// <param name="HealthLeft">The player's health fraction at the end (0 on a loss).</param>
/// <param name="FirstContact">Game seconds until the first damage either way; negative when
/// there was none.</param>
/// <param name="OtherDamage">Damage the arena's enemies took from anything but the player (a
/// guard, a companion): the fight was not a duel when this is above zero.</param>
public sealed record ArenaTrial(
    string Outcome,
    double Seconds,
    double Dealt,
    double Taken,
    double HealthLeft,
    int Swings,
    int Hits,
    int EnemyAttacks,
    int EnemyHits,
    int Blocked,
    int Staggers,
    int Parries,
    double FirstContact,
    double OtherDamage);

/// <summary>
/// The pure half of <c>--arena</c>: reading the roster, the bot's decision and the statistics.
/// The half that spawns and fights is <c>ArenaRunner</c>.
/// </summary>
public static class ArenaMath
{
    public const string Win = "win";
    public const string Loss = "loss";
    public const string Timeout = "timeout";

    /// <summary>
    /// Reads a roster: comma-separated ids, each optionally <c>id*N</c> for N at once. Returns
    /// null, or why it cannot be read. <c>all</c> and <c>bosses</c> are the caller's to expand.
    /// </summary>
    public static string? ParseRoster(string value, int defaultCount, out List<(string Id, int Count)> roster)
    {
        roster = new List<(string, int)>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string id = part;
            int count = defaultCount;
            int star = part.LastIndexOf('*');
            if (star >= 0)
            {
                id = part[..star].Trim();
                if (!int.TryParse(part[(star + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                    count < 1 || count > 12)
                {
                    return $"'{part}': the count after * must be a whole number from 1 to 12.";
                }
            }

            if (id.Length == 0)
            {
                return $"'{part}' names no enemy.";
            }

            roster.Add((id, Math.Clamp(count, 1, 12)));
        }

        return roster.Count == 0 ? "the roster is empty: name an enemy template, 'all' or 'bosses'." : null;
    }

    /// <summary>Reads a policy name; false when it is not one.</summary>
    public static bool TryParsePolicy(string? value, out ArenaPolicy policy)
    {
        policy = ArenaPolicy.Aggressive;
        return string.IsNullOrEmpty(value) || Enum.TryParse(value, ignoreCase: true, out policy) &&
            Enum.IsDefined(policy);
    }

    /// <summary>
    /// The bot's whole mind. It walks at a target that is further than <paramref name="attackGap"/>
    /// and swings at one that is not, unless it has almost no stamina left (it waits rather than
    /// winding itself). The guard policy holds block instead while a blow is coming and near enough
    /// to land.
    /// </summary>
    public static ArenaIntent Decide(ArenaPolicy policy, ArenaSense sense, float attackGap)
    {
        if (policy == ArenaPolicy.Passive)
        {
            return default;
        }

        bool inReach = sense.Gap <= attackGap;
        if (policy == ArenaPolicy.Guard && sense.TargetWindingUp && sense.Gap <= attackGap + 2f)
        {
            return new ArenaIntent(Forward: false, Attack: false, Block: true);
        }

        return new ArenaIntent(Forward: !inReach, Attack: inReach && sense.StaminaFraction > 0.15f, Block: false);
    }

    /// <summary>The value at fraction <paramref name="p"/> (0..1) of an ascending list, nearest
    /// rank; zero for an empty list.</summary>
    public static double Percentile(List<double> ascending, double p)
    {
        if (ascending.Count == 0)
        {
            return 0d;
        }

        int rank = (int)Math.Ceiling(Math.Clamp(p, 0d, 1d) * ascending.Count) - 1;
        return ascending[Math.Clamp(rank, 0, ascending.Count - 1)];
    }

    /// <summary>
    /// One matchup's trials as a compact row. Time to kill is over wins only (a loss has none);
    /// damage and rates are means over every trial. <c>flags</c> names what a reader should not
    /// miss: <c>no_contact</c>, <c>enemy_never_hit</c>, <c>player_never_hit</c>, <c>timeouts</c>,
    /// <c>third_party</c>.
    /// </summary>
    public static Dictionary<string, object?> Summarise(IReadOnlyList<ArenaTrial> trials)
    {
        int wins = 0;
        int losses = 0;
        int timeouts = 0;
        double dealt = 0d;
        double taken = 0d;
        double seconds = 0d;
        double other = 0d;
        double healthLeft = 1d;
        int swings = 0;
        int hits = 0;
        int enemyAttacks = 0;
        int enemyHits = 0;
        int blocked = 0;
        int staggers = 0;
        int parries = 0;
        var kills = new List<double>();
        foreach (ArenaTrial trial in trials)
        {
            switch (trial.Outcome)
            {
                case Win:
                    wins++;
                    kills.Add(trial.Seconds);
                    break;
                case Loss:
                    losses++;
                    break;
                default:
                    timeouts++;
                    break;
            }

            dealt += trial.Dealt;
            taken += trial.Taken;
            seconds += trial.Seconds;
            other += trial.OtherDamage;
            healthLeft = Math.Min(healthLeft, trial.HealthLeft);
            swings += trial.Swings;
            hits += trial.Hits;
            enemyAttacks += trial.EnemyAttacks;
            enemyHits += trial.EnemyHits;
            blocked += trial.Blocked;
            staggers += trial.Staggers;
            parries += trial.Parries;
        }

        kills.Sort();
        int n = Math.Max(1, trials.Count);
        var flags = new List<string>();
        if (trials.Count == 0)
        {
            // Nothing was fought, so there is nothing to flag.
        }
        else if (dealt <= 0d && taken <= 0d)
        {
            flags.Add("no_contact");
        }
        else
        {
            if (dealt <= 0d)
            {
                flags.Add("enemy_never_hit");
            }

            if (taken <= 0d)
            {
                flags.Add("player_never_hit");
            }
        }

        if (timeouts > 0)
        {
            flags.Add("timeouts");
        }

        if (other > 0d)
        {
            flags.Add("third_party");
        }

        return new Dictionary<string, object?>
        {
            ["trials"] = trials.Count,
            ["wins"] = wins,
            ["losses"] = losses,
            ["timeouts"] = timeouts,
            ["win_rate"] = Math.Round((double)wins / n, 3),
            ["ttk_s"] = new Dictionary<string, object?>
            {
                // Null, not 0, when the player never won: a zero reads as an instant kill.
                ["p50"] = kills.Count > 0 ? Math.Round(Percentile(kills, 0.5), 2) : null,
                ["min"] = kills.Count > 0 ? Math.Round(kills[0], 2) : null,
                ["max"] = kills.Count > 0 ? Math.Round(kills[^1], 2) : null,
            },
            ["dealt"] = Math.Round(dealt / n, 1),
            ["taken"] = Math.Round(taken / n, 1),
            ["dps"] = seconds > 0d ? Math.Round(dealt / seconds, 2) : 0d,
            ["enemy_dps"] = seconds > 0d ? Math.Round(taken / seconds, 2) : 0d,
            ["hp_left_min"] = Math.Round(healthLeft, 3),
            ["swings"] = swings,
            ["hits"] = hits,
            ["enemy_attacks"] = enemyAttacks,
            ["enemy_hits"] = enemyHits,
            ["blocked"] = blocked,
            ["staggers"] = staggers,
            ["parries"] = parries,
            ["flags"] = flags,
        };
    }

    /// <summary>One trial as a row, for <c>--json</c>.</summary>
    public static Dictionary<string, object?> Row(ArenaTrial trial) => new()
    {
        ["outcome"] = trial.Outcome,
        ["s"] = Math.Round(trial.Seconds, 2),
        ["dealt"] = Math.Round(trial.Dealt, 1),
        ["taken"] = Math.Round(trial.Taken, 1),
        ["hp_left"] = Math.Round(trial.HealthLeft, 3),
        ["swings"] = trial.Swings,
        ["hits"] = trial.Hits,
        ["enemy_attacks"] = trial.EnemyAttacks,
        ["enemy_hits"] = trial.EnemyHits,
        ["first_contact_s"] = Math.Round(trial.FirstContact, 2),
    };
}
