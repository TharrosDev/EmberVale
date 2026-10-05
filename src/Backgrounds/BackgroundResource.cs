using System.Collections.Generic;
using Embervale.Races;
using Godot;

namespace Embervale.Backgrounds;

/// <summary>
/// A character background as authored data: a soft nudge chosen at New Game, never a class.
/// It grants a tiny starting kit, a free tier-1 perk rank, a couple of story/standing hooks and at most
/// +/-1 on a stat or two; nothing is gated by it. <c>background.wayfarer</c> is the explicit no-op, equal
/// to what New Game did before backgrounds existed. Indexed by <see cref="BackgroundDatabase"/>; the
/// caps live in <see cref="BackgroundRules"/> and are enforced by <c>--validate</c>.
/// </summary>
[GlobalClass]
public partial class BackgroundResource : Resource
{
    /// <summary>Stable unique id, e.g. "background.soldier". The database key and the value stored in
    /// <c>CharacterProfile.Background</c>.</summary>
    [Export] public string Id { get; set; } = "background.unknown";

    /// <summary>Loc key for the display name.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Loc key for the one-paragraph description shown in the creator.</summary>
    [Export] public string DescKey { get; set; } = string.Empty;

    /// <summary>Tier-1 perk id granted free on New Game; empty for none. (The original six perks until the catalogue lands.)</summary>
    [Export] public string StartingPerkId { get; set; } = string.Empty;

    /// <summary>One of <see cref="BackgroundRules.LeanBranches"/> or empty. A creator badge only; it unlocks and blocks nothing.</summary>
    [Export] public string LeanBranch { get; set; } = string.Empty;

    /// <summary>Flat attribute deltas (|x| &lt;= 1). Untyped like <see cref="RaceResource.StatDeltas"/>; read via <see cref="StatDeltaList"/>.</summary>
    [Export] public Godot.Collections.Array StatDeltas { get; set; } = new();

    /// <summary>Kit entries <c>"item.id"</c> or <c>"item.id:count"</c>, added to the pack on New Game.</summary>
    [Export] public Godot.Collections.Array<string> StartingItems { get; set; } = new();

    /// <summary>Gold added to the pack on New Game.</summary>
    [Export] public int StartingGold { get; set; }

    /// <summary>Story flags set on New Game. Each needs a dialogue that reads it (validated).</summary>
    [Export] public Godot.Collections.Array<string> FlavorFlags { get; set; } = new();

    /// <summary>Starting-standing tweaks; untyped like <see cref="RaceResource.ReputationTweaks"/>; read via <see cref="ReputationTweakList"/>.</summary>
    [Export] public Godot.Collections.Array ReputationTweaks { get; set; } = new();

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
