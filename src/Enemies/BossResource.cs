using Godot;

namespace Embervale.Enemies;

/// <summary>
/// A boss fight's structure as authored data (Phase 36A): the HP-threshold phases it escalates
/// through, the abilities each phase grants, and the enrage fuse that stops it being out-waited.
/// Authored as a <c>.tres</c> under <c>data/bosses/</c> and indexed by <see cref="BossDatabase"/>.
///
/// Kept separate from <see cref="EnemyArchetypeResource"/>, which points at one by
/// <see cref="EnemyArchetypeResource.BossId"/> — the same shape as <c>AiProfileId</c> pointing at
/// <c>data/ai_profiles/</c>. The archetype stays "what this creature is made of"; this is "how its
/// fight is structured", and two bosses can share a shape without either owning it.
/// </summary>
[GlobalClass]
public partial class BossResource : Resource
{
    /// <summary>Stable id, e.g. <c>boss.iron_king</c> — what an archetype's <c>BossId</c> names.</summary>
    [Export] public string Id { get; set; } = "boss.unknown";

    /// <summary>The fight's stages, ordered high health to low. The first is the opening phase and
    /// its <c>HealthFraction</c> is <c>1.0</c>. A single-entry table is a legitimate boss: one stage,
    /// no escalation, still a <see cref="BossEntity"/> with a healthbar.</summary>
    [Export] public Godot.Collections.Array<BossPhaseResource> Phases { get; set; } = new();

    [ExportGroup("Enrage")]

    /// <summary>Seconds of fight before the enrage fires. <c>0</c> — the default — is no enrage.
    ///
    /// The clock starts on the <b>first damage traded with this boss</b>, not on the encounter event:
    /// <c>BossEncounterStartedEvent</c> is published only by <c>BossSummonComponent</c> (the Iron
    /// King's path), so keying off it would leave every lair boss with a fuse that never lit.</summary>
    [Export] public float EnrageSeconds { get; set; }

    /// <summary>Attack-speed bonus applied when the enrage fires, as a fraction.</summary>
    [Export] public float EnrageAttackSpeedBonus { get; set; } = 0.5f;

    /// <summary>Move-speed bonus applied when the enrage fires, as a fraction.</summary>
    [Export] public float EnrageMoveSpeedBonus { get; set; } = 0.25f;

    /// <summary>Spells granted when the enrage fires — the "you have taken too long" answer.</summary>
    [Export] public Godot.Collections.Array<string> EnrageSpellIds { get; set; } = new();

    /// <summary>Whether the enrage also drops the boss straight into its final phase, so a fight
    /// that has run long finishes at full intensity rather than in its opening stance.</summary>
    [Export] public bool EnrageForcesFinalPhase { get; set; } = true;
}
