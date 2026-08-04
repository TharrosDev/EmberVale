using Godot;

namespace Embervale.Enemies;

/// <summary>
/// One group of minions a boss phase brings into the fight (Phase 36D), authored as a sub-resource
/// inside a <see cref="BossPhaseResource"/>'s <c>AddWaves</c> — the same nesting as
/// <see cref="BossPhaseResource"/> inside a <see cref="BossResource"/>, or <see cref="HitZoneResource"/>
/// inside an archetype.
///
/// The wave names any registered enemy id, so <see cref="EnemyTemplateRegistry"/> builds it and no
/// new factory is involved: "phase three calls its cultists" is a few authored numbers, and any
/// creature already in the game can be somebody's adds.
/// </summary>
[GlobalClass]
public partial class BossAddWaveResource : Resource
{
    /// <summary>Which enemy arrives, e.g. <c>enemy.cinder_thrall</c>. Must be registered — the
    /// content validator checks it against <see cref="EnemyTemplateRegistry"/>, because an unknown id
    /// would otherwise spawn the fallback goblin into a boss arena and look like a design choice.</summary>
    [Export] public string TemplateId { get; set; } = string.Empty;

    /// <summary>How many arrive per wave.</summary>
    [Export] public int Count { get; set; } = 2;

    /// <summary>
    /// Seconds between re-summons while the phase lasts. <c>0</c> — the default — is a single wave on
    /// entering the phase. A repeating wave <b>must</b> set <see cref="MaxAlive"/>; the validator
    /// rejects one that does not, because a fight that ends by burying the player is not a fight.
    /// </summary>
    [Export] public float RepeatSeconds { get; set; }

    /// <summary>Most of this wave's adds alive at once. <c>0</c> is uncapped, legal only for a
    /// one-shot wave. A repeat tops the fight back up to this rather than stacking on it.</summary>
    [Export] public int MaxAlive { get; set; }

    /// <summary>Scales each add's health, the same knob <c>WorldEventDirector</c> applies to a hunt
    /// champion. <c>1</c> leaves the archetype as authored.</summary>
    [Export] public float HealthMultiplier { get; set; } = 1f;
}
