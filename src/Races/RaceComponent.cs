using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Races;

/// <summary>
/// Applies the player's chosen <see cref="CharacterProfile"/> race at spawn (Phase 26C): the
/// race's stat deltas become flat <see cref="StatModifier"/>s, and — for a freshly-created
/// character — its innate perks/spells are granted and its reputation tweaks applied. Added
/// <em>last</em> in <see cref="Player.PlayerFactory"/> so the sibling Stats/Perks/Spellcasting/
/// Reputation components have initialized by the time <see cref="OnInitialize"/> runs.
///
/// Stat deltas are sourced to this component and applied remove-then-add, so re-applying (the
/// dev <c>race</c> command) swaps cleanly. The starting grants are NOT re-derivable from data
/// after the fact (the player may have spent/changed them), so on a load they are restored by
/// the saved Perks/Spellcasting/Reputation state instead — the bootstrap passes
/// <see cref="ApplyStartingGrants"/> = false on the load path.
/// </summary>
[GlobalClass]
public partial class RaceComponent : EntityComponent
{
    /// <summary>The creation choices; defaults to Human until the bootstrap/creator sets it.</summary>
    public CharacterProfile Profile { get; set; } = CharacterProfile.Human;

    /// <summary>True on New Game (grant innate perks/spells/reputation); false on load (overlay restores them).</summary>
    public bool ApplyStartingGrants { get; set; } = true;

    private readonly List<StatType> _appliedStats = new();

    protected override void OnInitialize()
    {
        ApplyRaceInternal(grantStarting: ApplyStartingGrants, applyReputation: ApplyStartingGrants);
    }

    /// <summary>Dev/verification live-swap (<c>race &lt;id&gt;</c>): set the race and re-apply stat
    /// deltas + innate perks/spells. Reputation is intentionally skipped — re-adding the tweak would
    /// accumulate against the saved standing. Returns a short status line.</summary>
    public string SwapRaceForDebug(string raceId)
    {
        Profile.RaceId = raceId;
        // ponytail: dev tool — re-grants perks/spells (idempotent) and swaps stats; skips reputation
        // to avoid stacking the tweak on repeated swaps.
        if (RaceDatabase.Get(raceId) is not { } race)
        {
            return $"unknown race id: {raceId}";
        }

        ApplyRaceInternal(grantStarting: true, applyReputation: false);
        float maxHealth = Entity?.GetComponent<StatsComponent>()?.GetValue(StatType.Health) ?? 0f;
        return $"applied {race.DisplayName}: {race.StatDeltaList().Count} stat delta(s), max health {maxHealth:0}";
    }

    private void ApplyRaceInternal(bool grantStarting, bool applyReputation)
    {
        if (Entity is not { } owner)
        {
            return;
        }

        if (RaceDatabase.Get(Profile.RaceId) is not { } race)
        {
            Log.Warn($"RaceComponent: unknown race '{Profile.RaceId}'; nothing applied.");
            return;
        }

        if (owner.GetComponent<StatsComponent>() is { } stats)
        {
            foreach (StatType applied in _appliedStats)
            {
                stats.GetStat(applied).RemoveModifiersFromSource(this);
            }

            _appliedStats.Clear();
            foreach (RaceStatDelta delta in race.StatDeltaList())
            {
                if (delta.Amount == 0f)
                {
                    continue;
                }

                stats.GetStat(delta.Stat).AddModifier(new StatModifier(delta.Amount, ModifierType.Flat, this));
                _appliedStats.Add(delta.Stat);
            }

            stats.RefillResources();
        }

        if (!grantStarting)
        {
            return;
        }

        if (owner.GetComponent<PerksComponent>() is { } perks)
        {
            foreach (string perkId in race.InnatePerkIds)
            {
                if (PerkDatabase.Get(perkId) is { } perk)
                {
                    perks.GrantFree(perk);
                }
            }
        }

        if (owner.GetComponent<SpellcastingComponent>() is { } casting)
        {
            foreach (string spellId in race.InnateSpellIds)
            {
                if (SpellDatabase.Get(spellId) != null)
                {
                    casting.Learn(spellId);
                }
            }
        }

        if (applyReputation && owner.GetComponent<ReputationComponent>() is { } reputation)
        {
            foreach (RaceReputationTweak tweak in race.ReputationTweakList())
            {
                reputation.Add(tweak.FactionId, tweak.Amount);
            }
        }
    }
}
