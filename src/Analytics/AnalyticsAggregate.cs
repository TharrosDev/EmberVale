using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Embervale.Analytics;

/// <summary>
/// The running totals of one session, kept in memory and written once, in the <c>session_end</c>
/// row: damage by source and target (far too frequent to log per hit), gold in and out by source,
/// kills, the player's deaths by killer, experience, and how long each quest took in play seconds.
/// A reader needs only that last row for a summary. Pure, so the totals are unit-tested.
/// </summary>
public sealed class AnalyticsAggregate
{
    /// <summary>The name the player's own entity is recorded under.</summary>
    public const string Player = "player";

    /// <summary>Source/target pairs written to the row, largest total first.</summary>
    public const int DamagePairsWritten = 40;

    private readonly Dictionary<(string Source, string Target), (int Hits, double Total)> _damage = new();
    private readonly Dictionary<string, (int In, int Out)> _gold = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _kills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _deathsByKiller = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double Started, double? Seconds, string Result)> _quests = new(StringComparer.Ordinal);

    public int PlayerDeaths { get; private set; }

    public long Xp { get; private set; }

    /// <summary>True until anything at all has been counted.</summary>
    public bool IsEmpty =>
        _damage.Count == 0 && _gold.Count == 0 && _kills.Count == 0 && _quests.Count == 0 && PlayerDeaths == 0 && Xp == 0;

    public void Damage(string source, string target, double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0d)
        {
            return;
        }

        (int hits, double total) = _damage.GetValueOrDefault((source, target));
        _damage[(source, target)] = (hits + 1, total + amount);
    }

    /// <summary>Damage the named source has dealt to everything, for a test or a live readout.</summary>
    public double DamageFrom(string source) => _damage.Where(pair => pair.Key.Source == source).Sum(pair => pair.Value.Total);

    public void Death(string entity, string killer)
    {
        if (entity == Player)
        {
            PlayerDeaths++;
            string by = killer.Length == 0 ? "unknown" : killer;
            _deathsByKiller[by] = _deathsByKiller.GetValueOrDefault(by) + 1;
        }
        else if (killer == Player)
        {
            _kills[entity] = _kills.GetValueOrDefault(entity) + 1;
        }
    }

    public void Gold(int delta, string source)
    {
        if (delta == 0)
        {
            return;
        }

        (int gained, int spent) = _gold.GetValueOrDefault(source);
        _gold[source] = delta > 0 ? (gained + delta, spent) : (gained, spent - delta);
    }

    public int GoldIn => _gold.Values.Sum(value => value.In);

    public int GoldOut => _gold.Values.Sum(value => value.Out);

    public void AddXp(int amount) => Xp += Math.Max(0, amount);

    public void QuestStarted(string quest, double playSeconds) => _quests[quest] = (playSeconds, null, "open");

    /// <summary>Closes a quest as <paramref name="result"/> (<c>complete</c> or <c>failed</c>) and
    /// returns the play seconds since it started, or null when this session never saw it start
    /// (a quest carried in from a save).</summary>
    public double? QuestEnded(string quest, double playSeconds, string result)
    {
        if (!_quests.TryGetValue(quest, out (double Started, double? Seconds, string Result) entry) || entry.Seconds != null)
        {
            _quests[quest] = (playSeconds, null, result);
            return null;
        }

        double seconds = Math.Max(0d, playSeconds - entry.Started);
        _quests[quest] = (entry.Started, seconds, result);
        return seconds;
    }

    /// <summary>Writes the totals as properties of the object <paramref name="json"/> has open.</summary>
    public void WriteTo(Utf8JsonWriter json)
    {
        json.WriteNumber("player_deaths", PlayerDeaths);
        json.WriteNumber("xp", Xp);
        WriteCounts(json, "kills", _kills);
        WriteCounts(json, "deaths_by_killer", _deathsByKiller);

        json.WriteStartObject("gold");
        json.WriteNumber("in", GoldIn);
        json.WriteNumber("out", GoldOut);
        json.WriteStartObject("by_source");
        foreach ((string source, (int gained, int spent)) in _gold.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteStartObject(source);
            json.WriteNumber("in", gained);
            json.WriteNumber("out", spent);
            json.WriteEndObject();
        }

        json.WriteEndObject();
        json.WriteEndObject();

        json.WriteStartArray("damage");
        foreach (((string source, string target), (int hits, double total)) in
                 _damage.OrderByDescending(pair => pair.Value.Total).Take(DamagePairsWritten))
        {
            json.WriteStartObject();
            json.WriteString("source", source);
            json.WriteString("target", target);
            json.WriteNumber("hits", hits);
            json.WriteNumber("total", Math.Round(total, 1));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartObject("quests");
        foreach ((string quest, (double _, double? seconds, string result)) in _quests.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteStartObject(quest);
            json.WriteString("result", result);
            if (seconds is { } took)
            {
                json.WriteNumber("seconds", Math.Round(took, 1));
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter json, string name, Dictionary<string, int> counts)
    {
        json.WriteStartObject(name);
        foreach ((string key, int count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json.WriteNumber(key, count);
        }

        json.WriteEndObject();
    }
}
