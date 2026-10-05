namespace Embervale.Progression;

/// <summary>One non-stat effect of a perk: its kind, an optional qualifier (<see cref="Arg"/>, e.g. a
/// spell school; empty = applies everywhere) and the value one rank adds.</summary>
public readonly record struct PerkEffectEntry(PerkEffectKind Kind, string Arg, float ValuePerRank);
