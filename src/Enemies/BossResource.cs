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

    [ExportGroup("Encounter")]

    /// <summary>Seconds the player is held to watch the boss arrive. The lock leaves the world
    /// running (it is a cinematic hold, not a menu) — see <c>UiState.Open(pausesWorld: false)</c>.</summary>
    [Export] public float IntroLockSeconds { get; set; } = 2.5f;

    /// <summary>Seconds of slow-motion on the killing blow, and how slow.</summary>
    [Export] public float DefeatSlowSeconds { get; set; } = 1f;

    [Export] public float DefeatTimeScale { get; set; } = 0.35f;

    /// <summary>Music cue on defeat.</summary>
    [Export] public string DefeatMusicCue { get; set; } = "music.boss_defeat";

    [ExportGroup("Reward")]

    /// <summary>The guaranteed drop — a divine relic, for a fallen Flamebearer. Empty grants nothing,
    /// which is every boss whose payout is its hoard or a quest.</summary>
    [Export] public string RewardItemId { get; set; } = string.Empty;

    [Export] public int RewardQuantity { get; set; } = 1;

    /// <summary>
    /// Story flag set the first time this boss falls. It is what stops a reward being granted twice,
    /// so the validator requires one wherever a reward or a defeat conversation is authored —
    /// without it there is nothing to ask, and 36E exists because that question went unasked.
    ///
    /// Leave empty on a lair boss: <c>LairSpawnComponent.DefeatFlagId</c> already records those, and
    /// a second writer of the same fact is a drift waiting to happen.
    /// </summary>
    [Export] public string DefeatFlagId { get; set; } = string.Empty;

    /// <summary>Conversation opened once the defeat beat ends — the corruption choice, for a boss
    /// that offers one. Part of the reward, and gated with it.</summary>
    [Export] public string DefeatDialogueId { get; set; } = string.Empty;
}
