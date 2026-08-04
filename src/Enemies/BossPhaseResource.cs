using Godot;

namespace Embervale.Enemies;

/// <summary>
/// One stage of a boss fight (Phase 36A), authored as a sub-resource inside a
/// <see cref="BossResource"/>'s <c>Phases</c> — the same way <see cref="HitZoneResource"/> is
/// authored inside an archetype's <c>HitZones</c>, or <c>LootEntry</c> inside a loot table.
///
/// A phase is entered when the boss's health falls to or below <see cref="HealthFraction"/> and
/// never left: bosses escalate, they do not calm down. Entering applies the stat bonuses, grants any
/// abilities, and repaints the wind-up telegraph, so "phase 3 is faster, throws fire, and glows
/// hotter" is three fields rather than three branches in a controller.
/// </summary>
[GlobalClass]
public partial class BossPhaseResource : Resource
{
    /// <summary>Fraction of max health at or below which this phase begins. The opening phase is
    /// <c>1.0</c>; later phases descend (<c>0.66</c>, <c>0.33</c>). The content validator enforces
    /// the ordering, so the controller can trust it.</summary>
    [Export(PropertyHint.Range, "0,1")] public float HealthFraction { get; set; } = 1f;

    [ExportGroup("Escalation")]

    /// <summary>Attack-speed bonus as a fraction (<c>0.25</c> = +25%), applied as a
    /// <c>PercentMult</c> modifier under a per-phase source so a reload cannot stack it.</summary>
    [Export] public float AttackSpeedBonus { get; set; }

    /// <summary>Move-speed bonus as a fraction, applied the same way.</summary>
    [Export] public float MoveSpeedBonus { get; set; }

    /// <summary>Spells handed to the boss's <c>SpellcastingComponent</c> on entering this phase —
    /// the "per-phase ability set". Goes through the same grant path a dialogue reward uses, which
    /// ignores <c>PlayerLearnable</c>; mark monster spells accordingly so they never appear in the
    /// player's spellbook. Empty on a phase that only escalates numbers.</summary>
    [Export] public Godot.Collections.Array<string> GrantSpellIds { get; set; } = new();

    /// <summary>Swaps the AI profile on entry (e.g. a brute that starts kiting). Empty keeps the
    /// profile the archetype authored, which is the common case.</summary>
    [Export] public string AiProfileId { get; set; } = string.Empty;

    [ExportGroup("Telegraph")]

    /// <summary>Peak colour of the wind-up flare in this phase. Later phases usually run hotter so
    /// the escalation reads at a glance without a UI element.</summary>
    [Export] public Color TelegraphColor { get; set; } = new(1.0f, 0.25f, 0.05f);

    /// <summary>Peak emission energy of that flare.</summary>
    [Export] public float TelegraphEnergy { get; set; } = 2.5f;

    /// <summary>
    /// How much extra poise damage this boss takes while it is in its own attack wind-up (36C) —
    /// the knob that decides whether a phase's big telegraphed swing is something to punish or only
    /// something to dodge. <c>1</c> is no change; above <c>1</c> makes the wind-up a window worth
    /// attacking into; below <c>1</c> hardens it, for a phase meant to be survived rather than
    /// interrupted. Must stay positive: <c>0</c> would be a phase that can never be staggered while
    /// winding up, which is indistinguishable in play from the interrupt being broken.
    /// </summary>
    [Export] public float WindupPoiseMultiplier { get; set; } = 1f;
}
