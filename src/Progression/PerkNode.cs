using System.Collections.Generic;

namespace Embervale.Progression;

/// <summary>One perk reduced to the numbers the tree rules read, so the catalogue checks run without a
/// Godot resource (the validator builds these from <see cref="PerkResource"/>, the unit tests from the
/// authored <c>.tres</c> text).</summary>
public readonly record struct PerkNode(
    string Id,
    PerkBranch Branch,
    int Tier,
    int MaxRank,
    int Cost,
    int BranchPointsRequired,
    IReadOnlyList<string> Prerequisites,
    bool IsCapstone);
