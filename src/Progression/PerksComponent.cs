using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// Holds the perks an entity has learned and the ranks bought. Learning a rank
/// spends skill points from the <see cref="ProgressionComponent"/> and applies the
/// perk's bonus to the <see cref="StatsComponent"/> as a <see cref="StatModifier"/>
/// sourced to the <see cref="PerkResource"/> (recomputed on each rank-up and
/// re-applied on load). Persists learned ranks via <see cref="ISaveable"/>.
/// </summary>
[GlobalClass]
public partial class PerksComponent : EntityComponent, ISaveable
{
    private readonly Dictionary<string, int> _ranks = new();

    private ProgressionComponent? _progression;
    private StatsComponent? _stats;
    private CorruptionComponent? _corruption;

    public string SaveId => SaveKey("perks");

    protected override void OnInitialize()
    {
        _progression = Entity!.GetComponent<ProgressionComponent>();
        _stats = Entity.GetComponent<StatsComponent>();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public int RankOf(string perkId) => _ranks.TryGetValue(perkId, out int rank) ? rank : 0;

    /// <summary>The owner's current corruption tier (Untainted when it has no
    /// <see cref="CorruptionComponent"/>). Resolved from the sibling on demand so it is
    /// always current — mirrors how <c>ReputationComponent</c> reads corruption.</summary>
    private CorruptionTier CorruptionTierNow =>
        (_corruption ??= Entity?.GetComponent<CorruptionComponent>())?.Tier ?? CorruptionTier.Untainted;

    /// <summary>Whether the owner is corrupted enough to learn the perk (Phase 23H gate).</summary>
    public bool MeetsCorruption(PerkResource perk) => CorruptionTierNow >= perk.MinCorruptionTier;

    /// <summary>True if the perk has ranks left, the owner can afford the next one, and its
    /// corruption gate is met.</summary>
    public bool CanLearn(PerkResource perk)
    {
        if (perk == null || _progression == null)
        {
            return false;
        }

        return RankOf(perk.Id) < perk.MaxRank
            && _progression.SkillPoints >= perk.Cost
            && MeetsCorruption(perk);
    }

    /// <summary>Buys the next rank of a perk. Returns false if maxed or unaffordable.</summary>
    public bool Learn(PerkResource perk)
    {
        if (perk == null || _progression == null)
        {
            return false;
        }

        int rank = RankOf(perk.Id);
        if (rank >= perk.MaxRank || !MeetsCorruption(perk))
        {
            return false;
        }

        if (!_progression.SpendSkillPoints(perk.Cost))
        {
            return false;
        }

        rank++;
        _ranks[perk.Id] = rank;
        ApplyPerk(perk, rank);
        NotifyChanged(perk.Id, rank);
        return true;
    }

    /// <summary>Grants one rank of a perk for free — no skill-point cost — used for innate race
    /// perks (Phase 26C). Still respects <see cref="PerkResource.MaxRank"/> and corruption gating.</summary>
    public bool GrantFree(PerkResource perk)
    {
        if (perk == null)
        {
            return false;
        }

        int rank = RankOf(perk.Id);
        if (rank >= perk.MaxRank || !MeetsCorruption(perk))
        {
            return false;
        }

        rank++;
        _ranks[perk.Id] = rank;
        ApplyPerk(perk, rank);
        NotifyChanged(perk.Id, rank);
        return true;
    }

    private void ApplyPerk(PerkResource perk, int rank)
    {
        if (_stats == null)
        {
            return;
        }

        Stat target = _stats.GetStat(perk.Stat);
        target.RemoveModifiersFromSource(perk);
        float value = perk.ValueAtRank(rank);
        if (value != 0f)
        {
            target.AddModifier(new StatModifier(value, perk.ModifierType, perk));
        }
    }

    private void NotifyChanged(string perkId, int rank)
    {
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new PerkChangedEvent(Entity, perkId, rank));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var ranks = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in _ranks)
        {
            ranks[pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary { ["ranks"] = ranks };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Strip currently-applied perk bonuses before rebuilding from saved ranks.
        foreach (string perkId in _ranks.Keys)
        {
            if (PerkDatabase.Get(perkId) is { } perk && _stats != null)
            {
                _stats.GetStat(perk.Stat).RemoveModifiersFromSource(perk);
            }
        }

        _ranks.Clear();

        if (data.TryGetValue("ranks", out Variant ranksVar))
        {
            var ranks = ranksVar.AsGodotDictionary();
            foreach (Variant key in ranks.Keys)
            {
                string id = key.AsString();
                int rank = ranks[key].AsInt32();
                if (rank <= 0 || PerkDatabase.Get(id) is not { } perk)
                {
                    continue;
                }

                _ranks[id] = rank;
                ApplyPerk(perk, rank);
                NotifyChanged(id, rank);
            }
        }
    }
}
