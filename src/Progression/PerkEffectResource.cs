using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// One effect of a perk, authored as a sub-resource inside a perk <c>.tres</c> (the same pattern as
/// <c>RaceStatDelta</c> in a race). With <see cref="Kind"/> left at <see cref="PerkEffectKind.None"/> it
/// is an extra stat modifier (<see cref="Stat"/>, <see cref="ModifierType"/>); with any other kind it is
/// a non-stat effect read through <see cref="PerkQuery"/>. Either way one rank adds
/// <see cref="ValuePerRank"/>.
/// </summary>
[GlobalClass]
public partial class PerkEffectResource : Resource
{
    [Export] public PerkEffectKind Kind { get; set; } = PerkEffectKind.None;

    /// <summary>Qualifier for a non-stat effect (e.g. a spell school id); empty applies everywhere.</summary>
    [Export] public string Arg { get; set; } = string.Empty;

    [Export] public StatType Stat { get; set; } = StatType.Health;
    [Export] public ModifierType ModifierType { get; set; } = ModifierType.Flat;
    [Export] public float ValuePerRank { get; set; } = 1f;
}
