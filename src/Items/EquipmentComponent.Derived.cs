using System.Collections.Generic;
using Embervale.Stats;

namespace Embervale.Items;

/// <summary>
/// What the worn gear grants <em>as a whole</em>, re-derived from scratch after every equip,
/// unequip and load: flat mana and stamina regeneration, item-set threshold bonuses, and the unique
/// effects in force. Strip-then-rebuild on purpose: there is no incremental path to get out of step
/// with what is actually worn, and a load that restores "nothing equipped" removes everything the
/// abandoned timeline applied.
/// </summary>
public partial class EquipmentComponent
{
    /// <summary>The one source every set-bonus modifier is filed under, so they strip together.</summary>
    private readonly object _setSource = new();
    private readonly HashSet<StatType> _setStats = new();
    private readonly List<string> _setEffectIds = new();
    private Dictionary<string, int> _setPieces = new();
    private float _appliedManaRegen;
    private float _appliedStaminaRegen;

    /// <summary>Pieces worn per <c>set.*</c> id (only sets with at least one piece on).</summary>
    public IReadOnlyDictionary<string, int> SetPieces => _setPieces;

    /// <summary>How many pieces of <paramref name="setId"/> are worn.</summary>
    public int SetPiecesWorn(string setId) => _setPieces.TryGetValue(setId, out int worn) ? worn : 0;

    /// <summary>
    /// Every <c>unique.*</c> id in force: each worn item's own, then those granted by a set
    /// threshold. An id granted twice is listed twice; <see cref="UniqueEffectsComponent"/> decides
    /// what a duplicate means (it does not stack).
    /// </summary>
    public IEnumerable<string> ActiveUniqueEffectIds
    {
        get
        {
            foreach (ItemInstance instance in _equipped.Values)
            {
                if (instance.Template.UniqueEffectId.Length > 0)
                {
                    yield return instance.Template.UniqueEffectId;
                }
            }

            foreach (string id in _setEffectIds)
            {
                yield return id;
            }
        }
    }

    private void RefreshDerived()
    {
        RefreshRegeneration();
        RefreshSets();
    }

    /// <summary>
    /// Regeneration is a rate on <see cref="StatsComponent"/>, not a <see cref="StatType"/>, so it
    /// cannot ride a <see cref="StatModifier"/>. The total the gear grants is tracked here and only
    /// the difference from what was last applied is written, which leaves the actor's own base rate
    /// (and anything else that adjusts it) alone.
    /// </summary>
    private void RefreshRegeneration()
    {
        float mana = 0f;
        float stamina = 0f;
        foreach (ItemInstance instance in _equipped.Values)
        {
            if (instance.Equippable is { } equippable)
            {
                mana += equippable.BonusManaRegen;
                stamina += equippable.BonusStaminaRegen;
            }
        }

        if (_stats == null)
        {
            return;
        }

        _stats.ManaRegen += mana - _appliedManaRegen;
        _stats.StaminaRegen += stamina - _appliedStaminaRegen;
        _appliedManaRegen = mana;
        _appliedStaminaRegen = stamina;
    }

    private void RefreshSets()
    {
        if (_stats != null)
        {
            foreach (StatType stat in _setStats)
            {
                _stats.GetStat(stat).RemoveModifiersFromSource(_setSource);
            }
        }

        _setStats.Clear();
        _setEffectIds.Clear();
        _setPieces = SetRules.CountPieces(WornSetPieces());

        foreach (KeyValuePair<string, int> worn in _setPieces)
        {
            if (ItemSetDatabase.Get(worn.Key) is not { } set)
            {
                continue;
            }

            foreach (ItemSetBonusResource bonus in set.Bonuses)
            {
                if (bonus == null || !SetRules.IsActive(bonus.PiecesRequired, worn.Value))
                {
                    continue;
                }

                if (bonus.HasStat && _stats != null)
                {
                    _stats.GetStat(bonus.Stat).AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, _setSource));
                    _setStats.Add(bonus.Stat);
                }

                if (bonus.HasEffect)
                {
                    _setEffectIds.Add(bonus.EffectId);
                }
            }
        }
    }

    private IEnumerable<(string ItemId, string SetId)> WornSetPieces()
    {
        foreach (ItemInstance instance in _equipped.Values)
        {
            yield return (instance.TemplateId, instance.Template.SetId);
        }
    }
}
