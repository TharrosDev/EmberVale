using Embervale.Stats;
using Godot;

namespace Embervale.Races;

/// <summary>
/// One flat attribute delta a <see cref="RaceResource"/> grants its members: a signed amount on a
/// <see cref="StatType"/>. Authored as a sub-resource inside a race <c>.tres</c> (the same pattern as
/// <c>RecipeIngredient</c> in a recipe). Applied as an additive <see cref="StatModifier"/> at spawn
/// (Phase 26C), so races stay sparse — only the stats they actually move are listed.
/// </summary>
[GlobalClass]
public partial class RaceStatDelta : Resource
{
    [Export] public StatType Stat { get; set; } = StatType.Strength;

    /// <summary>Signed flat amount added to the base stat (e.g. +4 Strength, -1 MoveSpeed).</summary>
    [Export] public float Amount { get; set; }
}
