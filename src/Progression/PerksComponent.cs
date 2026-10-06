using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Races;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// Holds the perks an entity has learned and the ranks bought. Learning a rank
/// spends skill points from the <see cref="ProgressionComponent"/> and applies the
/// perk's stat bonuses to the <see cref="StatsComponent"/> as <see cref="StatModifier"/>s
/// sourced to the <see cref="PerkResource"/> (recomputed on each rank change and
/// re-applied on load); its non-stat effects are summed into <see cref="Effects"/>, which
/// gameplay reads through <see cref="PerkQuery"/>. Learning needs the perk's prerequisites,
/// enough points spent in its branch, and its corruption gate (<see cref="WhyNot"/> says which
/// is missing); <see cref="Respec"/> sells every bought rank back for gold. Ranks, free ranks,
/// points spent and respecs persist via <see cref="ISaveable"/> (the bookkeeping is
/// <see cref="PerkLedger"/>).
/// </summary>
[GlobalClass]
public partial class PerksComponent : EntityComponent, ISaveable
{
    private readonly PerkLedger _ledger = new();

    private ProgressionComponent? _progression;
    private StatsComponent? _stats;
    private CorruptionComponent? _corruption;

    public string SaveId => SaveKey("perks");

    /// <summary>The summed non-stat effects of every learned perk, rebuilt whenever a rank changes.</summary>
    public PerkEffectTotals Effects { get; } = new();

    /// <summary>Skill points invested in bought ranks (what a respec refunds).</summary>
    public int PointsSpent => _ledger.PointsSpent;

    public int RespecCount => _ledger.RespecCount;

    /// <summary>Gold the next respec costs; 0 when nothing is spent.</summary>
    public int RespecCost => RespecRules.Cost(_ledger.PointsSpent, _ledger.RespecCount);

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

    public int RankOf(string perkId) => _ledger.RankOf(perkId);

    /// <summary>Ranks of a perk that were granted rather than bought (a respec keeps these).</summary>
    public int FreeRankOf(string perkId) => _ledger.FreeOf(perkId);

    /// <summary>The owner's current corruption tier (Untainted when it has no
    /// <see cref="CorruptionComponent"/>). Resolved from the sibling on demand so it is
    /// always current — mirrors how <c>ReputationComponent</c> reads corruption.</summary>
    private CorruptionTier CorruptionTierNow =>
        (_corruption ??= Entity?.GetComponent<CorruptionComponent>())?.Tier ?? CorruptionTier.Untainted;

    /// <summary>Whether the owner is corrupted enough to learn the perk (Phase 23H gate).</summary>
    public bool MeetsCorruption(PerkResource perk) => CorruptionTierNow >= perk.MinCorruptionTier;

    /// <summary>True when every prerequisite perk has at least one rank.</summary>
    public bool PrerequisitesMet(PerkResource perk)
    {
        foreach (string id in perk.PrerequisiteIds)
        {
            if (RankOf(id) <= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Skill points spent on bought ranks of perks in <paramref name="branch"/>.</summary>
    public int BranchPoints(PerkBranch branch)
    {
        int points = 0;
        foreach (KeyValuePair<string, int> pair in _ledger.Ranks)
        {
            if (PerkDatabase.Get(pair.Key) is { } perk && perk.Branch == branch)
            {
                points += (pair.Value - _ledger.FreeOf(pair.Key)) * perk.Cost;
            }
        }

        return points;
    }

    /// <summary>The first reason the next rank of <paramref name="perk"/> cannot be bought, or
    /// <see cref="PerkBlock.None"/>.</summary>
    public PerkBlock WhyNot(PerkResource perk)
    {
        if (perk == null || _progression == null)
        {
            return PerkBlock.Maxed;
        }

        return PerkRules.WhyNot(
            RankOf(perk.Id), perk.MaxRank, MeetsCorruption(perk), PrerequisitesMet(perk),
            BranchPoints(perk.Branch), perk.BranchPointsRequired, _progression.SkillPoints, perk.Cost);
    }

    /// <summary>True if the next rank can be bought now.</summary>
    public bool CanLearn(PerkResource perk) => WhyNot(perk) == PerkBlock.None;

    /// <summary>Buys the next rank of a perk. Returns false if <see cref="WhyNot"/> names a reason.</summary>
    public bool Learn(PerkResource perk)
    {
        if (!CanLearn(perk) || !_progression!.SpendSkillPoints(perk.Cost))
        {
            return false;
        }

        _ledger.AddPaid(perk.Id, perk.Cost);
        RankChanged(perk);
        return true;
    }

    /// <summary>Grants one rank of a perk for free — no skill-point cost — used for innate race
    /// perks (Phase 26C). Still respects <see cref="PerkResource.MaxRank"/> and corruption gating, but
    /// not prerequisites; a respec keeps the rank.</summary>
    public bool GrantFree(PerkResource perk)
    {
        if (perk == null || RankOf(perk.Id) >= perk.MaxRank || !MeetsCorruption(perk))
        {
            return false;
        }

        _ledger.AddFree(perk.Id);
        RankChanged(perk);
        return true;
    }

    /// <summary>Whether <paramref name="wallet"/> holds the gold for a respec and there is a rank to sell back.</summary>
    public bool CanRespec(InventoryComponent wallet)
    {
        return _progression != null
            && _ledger.PointsSpent > 0
            && wallet.CountOf(GameIds.Currency.Gold) >= RespecCost;
    }

    /// <summary>Sells every bought rank back: charges <see cref="RespecCost"/> gold from
    /// <paramref name="wallet"/>, keeps free ranks, and refunds the points spent to the skill pool.</summary>
    public bool Respec(InventoryComponent wallet)
    {
        if (!CanRespec(wallet) || !wallet.RemoveItem(GameIds.Currency.Gold, RespecCost))
        {
            return false;
        }

        var before = new List<string>(_ledger.Ranks.Keys);
        StripStatModifiers();
        _progression!.RefundSkillPoints(_ledger.Respec());
        ApplyAll();
        foreach (string perkId in before)
        {
            NotifyChanged(perkId, _ledger.RankOf(perkId));
        }

        return true;
    }

    private void RankChanged(PerkResource perk)
    {
        ApplyPerk(perk, _ledger.RankOf(perk.Id));
        RebuildEffects();
        NotifyChanged(perk.Id, _ledger.RankOf(perk.Id));
    }

    private void ApplyPerk(PerkResource perk, int rank)
    {
        if (_stats == null)
        {
            return;
        }

        RemoveStatModifiers(perk);
        AddStatModifier(perk.Stat, perk.ModifierType, perk.ValueAtRank(rank), perk);
        foreach (PerkEffectResource effect in perk.EffectList())
        {
            if (effect.Kind == PerkEffectKind.None)
            {
                AddStatModifier(effect.Stat, effect.ModifierType, effect.ValuePerRank * rank, perk);
            }
        }
    }

    private void AddStatModifier(StatType stat, ModifierType type, float value, PerkResource source)
    {
        if (value != 0f)
        {
            _stats!.GetStat(stat).AddModifier(new StatModifier(value, type, source));
        }
    }

    private void RemoveStatModifiers(PerkResource perk)
    {
        _stats!.GetStat(perk.Stat).RemoveModifiersFromSource(perk);
        foreach (PerkEffectResource effect in perk.EffectList())
        {
            if (effect.Kind == PerkEffectKind.None)
            {
                _stats.GetStat(effect.Stat).RemoveModifiersFromSource(perk);
            }
        }
    }

    private void StripStatModifiers()
    {
        if (_stats == null)
        {
            return;
        }

        foreach (string perkId in _ledger.Ranks.Keys)
        {
            if (PerkDatabase.Get(perkId) is { } perk)
            {
                RemoveStatModifiers(perk);
            }
        }
    }

    /// <summary>Re-applies every held rank's stat modifiers and rebuilds <see cref="Effects"/>.</summary>
    private void ApplyAll()
    {
        foreach (KeyValuePair<string, int> pair in _ledger.Ranks)
        {
            if (PerkDatabase.Get(pair.Key) is { } perk)
            {
                ApplyPerk(perk, pair.Value);
            }
        }

        RebuildEffects();
    }

    private void RebuildEffects()
    {
        Effects.Clear();
        foreach (KeyValuePair<string, int> pair in _ledger.Ranks)
        {
            if (PerkDatabase.Get(pair.Key) is { } perk)
            {
                foreach (PerkEffectEntry entry in perk.EffectEntries())
                {
                    Effects.Add(entry, pair.Value);
                }
            }
        }
    }

    private void NotifyChanged(string perkId, int rank)
    {
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new PerkChangedEvent(Entity, perkId, rank));
        }
    }

    /// <summary>Whether the owner's race grants the perk at creation — how an older save, which did not
    /// record free ranks, tells an innate rank from a bought one.</summary>
    private bool IsInnate(string perkId)
    {
        return Entity?.GetComponent<RaceComponent>() is { } race
            && RaceDatabase.Get(race.Profile.RaceId) is { } def
            && def.InnatePerkIds.Contains(perkId);
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var ranks = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in _ledger.Ranks)
        {
            ranks[pair.Key] = pair.Value;
        }

        var free = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in _ledger.FreeRanks)
        {
            free[pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary
        {
            ["ranks"] = ranks,
            ["free"] = free,
            ["spent"] = _ledger.PointsSpent,
            ["respecs"] = _ledger.RespecCount,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Strip currently-applied perk bonuses before rebuilding from saved ranks.
        var before = new List<string>(_ledger.Ranks.Keys);
        StripStatModifiers();

        var ranks = new Dictionary<string, int>();
        if (data.TryGetValue("ranks", out Variant ranksVar))
        {
            var saved = ranksVar.AsGodotDictionary();
            foreach (Variant key in saved.Keys)
            {
                string id = key.AsString();
                if (PerkDatabase.Get(id) != null)
                {
                    ranks[id] = saved[key].AsInt32();
                }
            }
        }

        Dictionary<string, int>? free = null;
        if (data.TryGetValue("free", out Variant freeVar))
        {
            free = new Dictionary<string, int>();
            var saved = freeVar.AsGodotDictionary();
            foreach (Variant key in saved.Keys)
            {
                free[key.AsString()] = saved[key].AsInt32();
            }
        }

        _ledger.Restore(
            ranks,
            free,
            data.TryGetValue("spent", out Variant spentVar) ? spentVar.AsInt32() : null,
            data.TryGetValue("respecs", out Variant respecVar) ? respecVar.AsInt32() : null,
            id => PerkDatabase.Get(id)?.Cost ?? 0,
            IsInnate);

        ApplyAll();

        // Tell listeners about everything that was or is held; a perk that fell out reports rank 0.
        foreach (string id in before)
        {
            if (_ledger.RankOf(id) == 0)
            {
                NotifyChanged(id, 0);
            }
        }

        foreach (KeyValuePair<string, int> pair in _ledger.Ranks)
        {
            NotifyChanged(pair.Key, pair.Value);
        }
    }
}
