using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// One row of an <see cref="ItemSetResource"/>: something the wearer gains once
/// <see cref="PiecesRequired"/> pieces of the set are equipped. A row is a stat modifier
/// (<see cref="Value"/> non-zero), a <see cref="UniqueEffectResource"/> (<see cref="EffectId"/>
/// non-empty), or both. A threshold that grants several stats is several rows sharing one
/// <see cref="PiecesRequired"/>, which keeps the <c>.tres</c> one sub-resource deep.
/// </summary>
[GlobalClass]
public partial class ItemSetBonusResource : Resource
{
    /// <summary>How many equipped pieces switch this row on (2 or more).</summary>
    [Export] public int PiecesRequired { get; set; } = 2;

    [Export] public StatType Stat { get; set; } = StatType.Armor;

    /// <summary>The modifier amount; 0 means this row carries no stat modifier.</summary>
    [Export] public float Value { get; set; }

    [Export] public ModifierType ModifierType { get; set; } = ModifierType.Flat;

    /// <summary>Optional <c>unique.*</c> id granted at this threshold; empty for none.</summary>
    [Export] public string EffectId { get; set; } = string.Empty;

    public bool HasStat => Value != 0f;

    public bool HasEffect => !string.IsNullOrEmpty(EffectId);
}
