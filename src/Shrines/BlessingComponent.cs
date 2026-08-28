using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Shrines;

/// <summary>
/// The player's persistent record of claimed shrine blessings. It saves only stable shrine ids;
/// the passive stat modifiers are re-derived from the resource database on every load, leaving no
/// second authority for the same fact and ensuring a load replaces the prior session's blessings.
/// </summary>
[GlobalClass]
public partial class BlessingComponent : EntityComponent, ISaveable
{
    private readonly HashSet<string> _claimedShrineIds = new();
    private StatsComponent? _stats;

    public string SaveId => SaveKey("blessings");

    public IReadOnlyCollection<string> ClaimedShrineIds => _claimedShrineIds;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public bool HasClaimed(string shrineId) => _claimedShrineIds.Contains(shrineId);

    /// <summary>Claims and applies a shrine exactly once. This is the only mutation path, so every
    /// caller gets persistence, stat application and player feedback together.</summary>
    public bool TryClaim(ShrineResource shrine)
    {
        if (shrine == null || _stats == null || !BlessingRules.TryClaim(_claimedShrineIds, shrine.Id))
        {
            return false;
        }

        Apply(shrine);
        EventBus.Instance?.Publish(new BlessingClaimedEvent(Entity!, shrine));
        return true;
    }

    private void Apply(ShrineResource shrine)
    {
        Stat target = _stats!.GetStat(shrine.Stat);
        target.RemoveModifiersFromSource(shrine.Id);
        target.AddModifier(new StatModifier(shrine.Value, shrine.ModifierType, shrine.Id));
    }

    private void RemoveAppliedBlessings()
    {
        if (_stats == null)
        {
            return;
        }

        foreach (string shrineId in _claimedShrineIds)
        {
            // A removed or malformed resource must not leave its old modifier live. Strip the stable
            // source from every stat rather than depending on today's resource lookup to succeed.
            foreach (StatType stat in System.Enum.GetValues<StatType>())
            {
                _stats.GetStat(stat).RemoveModifiersFromSource(shrineId);
            }
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var claims = new Godot.Collections.Array();
        foreach (string shrineId in _claimedShrineIds)
        {
            claims.Add(shrineId);
        }

        return new Godot.Collections.Dictionary { ["claims"] = claims };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        RemoveAppliedBlessings();

        var restored = new List<string>();
        if (data.TryGetValue("claims", out Variant claimsVar))
        {
            foreach (Variant entry in claimsVar.AsGodotArray())
            {
                restored.Add(entry.AsString());
            }
        }

        BlessingRules.ReplaceClaims(_claimedShrineIds, restored);

        // A deleted/renamed resource cannot safely recreate a passive. Match the established perk
        // compatibility policy: drop the unknown id after replacement rather than merging it over
        // a live modifier from the previous session.
        foreach (string shrineId in new List<string>(_claimedShrineIds))
        {
            if (ShrineDatabase.Get(shrineId) is { } shrine)
            {
                Apply(shrine);
            }
            else
            {
                _claimedShrineIds.Remove(shrineId);
            }
        }
    }
}
