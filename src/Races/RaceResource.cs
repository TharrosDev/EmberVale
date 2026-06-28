using System.Collections.Generic;
using Godot;

namespace Embervale.Races;

/// <summary>
/// A playable race as authored data: identity, flat attribute deltas, innate perks/abilities, starting
/// reputation tweaks, and appearance option ids. Indexed by <see cref="RaceDatabase"/> and consumed at
/// spawn by <c>PlayerFactory</c> (Phase 26C) — composition over inheritance, so a new race is a
/// <c>.tres</c> under <c>data/races/</c> with no code change.
/// </summary>
[GlobalClass]
public partial class RaceResource : Resource
{
    /// <summary>Stable unique id, e.g. "race.human". The database key.</summary>
    [Export] public string Id { get; set; } = "race.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown";

    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

    /// <summary>Flat attribute deltas. Untyped so authored sub-resource arrays bind cleanly; elements
    /// are read back as <see cref="RaceStatDelta"/> via <see cref="StatDeltaList"/>.</summary>
    [Export] public Godot.Collections.Array StatDeltas { get; set; } = new();

    /// <summary>Perk ids (<see cref="Embervale.Progression.PerkDatabase"/>) granted at creation.</summary>
    [Export] public Godot.Collections.Array<string> InnatePerkIds { get; set; } = new();

    /// <summary>Spell/ability ids (<see cref="Embervale.Magic.SpellDatabase"/>) seeded at creation,
    /// e.g. the Draekyn dragon ability.</summary>
    [Export] public Godot.Collections.Array<string> InnateSpellIds { get; set; } = new();

    /// <summary>Starting reputation tweaks. Untyped; read back as <see cref="RaceReputationTweak"/> via
    /// <see cref="ReputationTweakList"/>.</summary>
    [Export] public Godot.Collections.Array ReputationTweaks { get; set; } = new();

    /// <summary>Appearance option ids the creator (Phase 26D) offers for this race.</summary>
    [Export] public Godot.Collections.Array<string> AppearanceOptionIds { get; set; } = new();

    /// <summary>The stat deltas read back as their concrete type, skipping malformed elements.</summary>
    public List<RaceStatDelta> StatDeltaList()
    {
        var list = new List<RaceStatDelta>();
        foreach (Variant element in StatDeltas)
        {
            if (element.As<RaceStatDelta>() is { } delta)
            {
                list.Add(delta);
            }
        }

        return list;
    }

    /// <summary>The reputation tweaks read back as their concrete type, skipping malformed elements.</summary>
    public List<RaceReputationTweak> ReputationTweakList()
    {
        var list = new List<RaceReputationTweak>();
        foreach (Variant element in ReputationTweaks)
        {
            if (element.As<RaceReputationTweak>() is { } tweak)
            {
                list.Add(tweak);
            }
        }

        return list;
    }
}
