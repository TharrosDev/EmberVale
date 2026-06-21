using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Factions;

/// <summary>
/// Tracks the player's standing with every faction and turns kills into reputation
/// shifts. Each faction starts at its <see cref="FactionResource.DefaultReputation"/>;
/// killing a faction's member lowers standing with that faction and propagates through
/// the web — pleasing its enemies and angering its allies. Standing maps to a
/// <see cref="ReputationTier"/>, which other systems (enemy AI, dialogue, UI) read to
/// decide hostility. Persists the per-faction values via <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class ReputationComponent : EntityComponent, ISaveable
{
    private readonly Dictionary<string, int> _reputation = new();

    public string SaveId => SaveKey("reputation");

    protected override void OnInitialize()
    {
        foreach (FactionResource faction in FactionDatabase.All)
        {
            _reputation[faction.Id] = faction.DefaultReputation;
        }

        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        SaveManager.Instance?.Register(this);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Current standing with a faction (its default if never adjusted).</summary>
    public int Get(string factionId)
    {
        if (_reputation.TryGetValue(factionId, out int value))
        {
            return value;
        }

        return FactionDatabase.Get(factionId)?.DefaultReputation ?? 0;
    }

    public ReputationTier TierOf(string factionId) => ReputationTiers.Of(Get(factionId));

    /// <summary>Whether a faction's members currently treat the player as an enemy.</summary>
    public bool IsHostile(string factionId)
    {
        FactionResource? faction = FactionDatabase.Get(factionId);
        if (faction == null)
        {
            return false;
        }

        return TierOf(factionId) <= faction.HostileThreshold;
    }

    /// <summary>Adjusts standing with a faction, clamped, and announces the change.</summary>
    public void Add(string factionId, int delta)
    {
        if (string.IsNullOrEmpty(factionId) || delta == 0)
        {
            return;
        }

        int updated = Mathf.Clamp(Get(factionId) + delta, ReputationTiers.Min, ReputationTiers.Max);
        if (_reputation.TryGetValue(factionId, out int current) && current == updated)
        {
            return;
        }

        _reputation[factionId] = updated;
        EventBus.Instance?.Publish(new ReputationChangedEvent(factionId, updated, ReputationTiers.Of(updated)));
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        // Only kills the player landed shift the player's reputation.
        if (Entity == null || e.Killer == null || !ReferenceEquals(e.Killer, Entity))
        {
            return;
        }

        if (e.Entity.GetComponent<FactionComponent>()?.Faction is not { } faction)
        {
            return;
        }

        int penalty = Mathf.Max(1, faction.KillReputationPenalty);
        Add(faction.Id, -penalty);

        // Harming a faction echoes through its web.
        int spread = Mathf.Max(1, penalty / 2);
        foreach (string enemyId in faction.Enemies)
        {
            Add(enemyId, spread);
        }

        foreach (string allyId in faction.Allies)
        {
            Add(allyId, -spread);
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var values = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in _reputation)
        {
            values[pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary { ["reputation"] = values };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("reputation", out Variant valuesVar))
        {
            return;
        }

        var values = valuesVar.AsGodotDictionary();
        foreach (Variant key in values.Keys)
        {
            string factionId = key.AsString();
            int value = Mathf.Clamp(values[key].AsInt32(), ReputationTiers.Min, ReputationTiers.Max);
            _reputation[factionId] = value;
            EventBus.Instance?.Publish(new ReputationChangedEvent(factionId, value, ReputationTiers.Of(value)));
        }
    }
}
