using System.Collections.Generic;
using Embervale.Corruption;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// A designer-authored perk: a rankable passive that boosts one stat. Each rank
/// costs <see cref="Cost"/> skill points and adds <see cref="ValuePerRank"/> of the
/// target <see cref="Stat"/> (so rank R grants <c>ValuePerRank × R</c>). Authored as
/// a <c>.tres</c> under <c>data/perks/</c> and indexed by <see cref="PerkDatabase"/>;
/// a <see cref="PerksComponent"/> applies the bonus as a <see cref="StatModifier"/>.
///
/// A perk can also sit in a tree (<see cref="Branch"/>, <see cref="Tier"/>, <see cref="Column"/>,
/// <see cref="PrerequisiteIds"/>, <see cref="BranchPointsRequired"/>) and carry extra
/// <see cref="Effects"/>: more stat modifiers, or the non-stat <see cref="PerkEffectKind"/>s.
/// Every v2 field is defaulted, so the original single-stat <c>.tres</c> files load unchanged.
/// A perk that only has <see cref="Effects"/> leaves <see cref="ValuePerRank"/> at 0.
/// </summary>
[GlobalClass]
public partial class PerkResource : Resource
{
    /// <summary>Stable id, e.g. "perk.toughness". The save/database key.</summary>
    [Export] public string Id { get; set; } = "perk.unknown";

    /// <summary>Fallback name; the shown name is <see cref="LocalizedName"/>.</summary>
    [Export] public string DisplayName { get; set; } = "Unknown Perk";

    /// <summary>Fallback text; the shown text is <see cref="LocalizedDescription"/>.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Maximum number of ranks that can be purchased.</summary>
    [Export] public int MaxRank { get; set; } = 1;

    /// <summary>Skill-point cost per rank.</summary>
    [Export] public int Cost { get; set; } = 1;

    [Export] public StatType Stat { get; set; } = StatType.Health;
    [Export] public ModifierType ModifierType { get; set; } = ModifierType.Flat;
    /// <summary>Value of <see cref="Stat"/> per rank; 0 means this perk has no stat bonus of its own.</summary>
    [Export] public float ValuePerRank { get; set; }

    /// <summary>Minimum corruption tier required to learn this perk (Phase 23H).
    /// <see cref="CorruptionTier.Untainted"/> (the default) leaves a perk ungated; a higher
    /// value marks it a corrupted passive unlocked only by corruption.</summary>
    [Export] public CorruptionTier MinCorruptionTier { get; set; } = CorruptionTier.Untainted;

    // --- v2: the tree ------------------------------------------------------

    [Export] public PerkBranch Branch { get; set; } = PerkBranch.None;

    /// <summary>Row in the branch, 1 (entry) to <see cref="PerkRules.MaxTier"/>.</summary>
    [Export] public int Tier { get; set; } = 1;

    /// <summary>0-based column within the tier, for layout only.</summary>
    [Export] public int Column { get; set; }

    /// <summary>Perk ids that must each have at least one rank before this one can be learned.</summary>
    [Export] public Godot.Collections.Array<string> PrerequisiteIds { get; set; } = new();

    /// <summary>Skill points that must already be spent in <see cref="Branch"/> before this perk unlocks.</summary>
    [Export] public int BranchPointsRequired { get; set; }

    /// <summary>The branch's top perk. Authoring flag for the UI and the catalogue checks.</summary>
    [Export] public bool IsCapstone { get; set; }

    /// <summary>Extra effects, each a <see cref="PerkEffectResource"/> (read through
    /// <see cref="EffectList"/> / <see cref="EffectEntries"/>).</summary>
    [Export] public Godot.Collections.Array Effects { get; set; } = new();

    /// <summary>Locale key of the perk name: <c>&lt;id&gt;.name</c>.</summary>
    public string NameKey => Id + ".name";

    /// <summary>Locale key of the perk description: <c>&lt;id&gt;.desc</c>.</summary>
    public string DescKey => Id + ".desc";

    public string LocalizedName => Loc.Has(NameKey) ? Loc.T(NameKey) : DisplayName;

    public string LocalizedDescription => Loc.Has(DescKey) ? Loc.T(DescKey) : Description;

    /// <summary>The authored <see cref="Effects"/> as typed resources.</summary>
    public List<PerkEffectResource> EffectList()
    {
        var list = new List<PerkEffectResource>();
        foreach (Variant element in Effects)
        {
            if (element.As<PerkEffectResource>() is { } effect)
            {
                list.Add(effect);
            }
        }

        return list;
    }

    /// <summary>The non-stat effects (<see cref="PerkEffectKind"/> other than None) as plain values.</summary>
    public List<PerkEffectEntry> EffectEntries()
    {
        var list = new List<PerkEffectEntry>();
        foreach (PerkEffectResource effect in EffectList())
        {
            if (effect.Kind != PerkEffectKind.None)
            {
                list.Add(new PerkEffectEntry(effect.Kind, effect.Arg, effect.ValuePerRank));
            }
        }

        return list;
    }

    /// <summary>The cumulative bonus value granted at the given rank.</summary>
    public float ValueAtRank(int rank) => ValuePerRank * rank;
}
