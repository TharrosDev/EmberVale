using System;
using System.Collections.Generic;

namespace Embervale.Progression;

/// <summary>
/// The pure bookkeeping behind <see cref="PerksComponent"/>: ranks per perk, which of them were free
/// (innate race perks, dev grants) and which were bought, the skill points spent and the respecs taken.
/// Godot-free so the save restore and the respec arithmetic are unit-testable.
/// </summary>
public sealed class PerkLedger
{
    private readonly Dictionary<string, int> _ranks = new();
    private readonly Dictionary<string, int> _free = new();

    /// <summary>Skill points currently invested in bought ranks.</summary>
    public int PointsSpent { get; private set; }

    public int RespecCount { get; private set; }

    public IReadOnlyDictionary<string, int> Ranks => _ranks;

    public IReadOnlyDictionary<string, int> FreeRanks => _free;

    public int RankOf(string perkId) => _ranks.TryGetValue(perkId, out int rank) ? rank : 0;

    public int FreeOf(string perkId) => _free.TryGetValue(perkId, out int free) ? free : 0;

    /// <summary>Records one bought rank.</summary>
    public void AddPaid(string perkId, int cost)
    {
        _ranks[perkId] = RankOf(perkId) + 1;
        PointsSpent += Math.Max(0, cost);
    }

    /// <summary>Records one rank that cost nothing; a respec keeps it.</summary>
    public void AddFree(string perkId)
    {
        _ranks[perkId] = RankOf(perkId) + 1;
        _free[perkId] = FreeOf(perkId) + 1;
    }

    /// <summary>Drops every bought rank, keeps the free ones and returns the points to refund.</summary>
    public int Respec()
    {
        int refund = PointsSpent;
        foreach (string perkId in new List<string>(_ranks.Keys))
        {
            int kept = FreeOf(perkId);
            if (kept > 0)
            {
                _ranks[perkId] = kept;
            }
            else
            {
                _ranks.Remove(perkId);
            }
        }

        PointsSpent = 0;
        RespecCount++;
        return refund;
    }

    public void Clear()
    {
        _ranks.Clear();
        _free.Clear();
        PointsSpent = 0;
        RespecCount = 0;
    }

    /// <summary>
    /// Replaces everything with a saved state, never merging over what is live. The save keys added in
    /// v2 may be absent (<c>null</c>) in an older save: absent <paramref name="free"/> means the owner's
    /// innate perks (<paramref name="isInnate"/>) were each one free rank, absent
    /// <paramref name="spent"/> means every non-free rank was bought at its <paramref name="costOf"/>,
    /// absent <paramref name="respecs"/> means none. Prerequisites are deliberately not checked: a save is
    /// a record of what was true, not a purchase.
    /// </summary>
    public void Restore(
        IEnumerable<KeyValuePair<string, int>> ranks,
        IReadOnlyDictionary<string, int>? free,
        int? spent,
        int? respecs,
        Func<string, int> costOf,
        Func<string, bool> isInnate)
    {
        Clear();
        foreach (KeyValuePair<string, int> pair in ranks)
        {
            if (pair.Value > 0)
            {
                _ranks[pair.Key] = pair.Value;
            }
        }

        foreach (KeyValuePair<string, int> pair in _ranks)
        {
            int saved = free != null
                ? (free.TryGetValue(pair.Key, out int f) ? f : 0)
                : (isInnate(pair.Key) ? 1 : 0);
            int kept = Math.Clamp(saved, 0, pair.Value);
            if (kept > 0)
            {
                _free[pair.Key] = kept;
            }
        }

        if (spent.HasValue)
        {
            PointsSpent = Math.Max(0, spent.Value);
        }
        else
        {
            foreach (KeyValuePair<string, int> pair in _ranks)
            {
                PointsSpent += (pair.Value - FreeOf(pair.Key)) * Math.Max(0, costOf(pair.Key));
            }
        }

        RespecCount = Math.Max(0, respecs ?? 0);
    }
}
