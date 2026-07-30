using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored dynamic encounter: a group of enemies the
/// <see cref="EncounterDirector"/> can spawn near the player, gated by time of day.
/// Authored as a <c>.tres</c> under <c>data/encounters/</c> and indexed by
/// <see cref="EncounterDatabase"/> — a new encounter is a new resource, no code.
///
/// This is deliberately lightweight (a weighted, phase-gated spawn); the richer
/// "world event" framework — named events with objectives and rewards — is Phase 17.
/// </summary>
[GlobalClass]
public partial class EncounterResource : Resource
{
    /// <summary>Stable id, e.g. "encounter.goblin_patrol".</summary>
    [Export] public string Id { get; set; } = "encounter.unknown";

    [Export] public string DisplayName { get; set; } = "Encounter";

    /// <summary>Archetype id of the enemy to spawn (currently the goblin factory).</summary>
    [Export] public string EnemyTemplateId { get; set; } = "enemy.goblin"; // mirrors GameIds.Enemies.Goblin

    [Export] public int MinCount { get; set; } = 1;
    [Export] public int MaxCount { get; set; } = 2;

    /// <summary>Relative likelihood of being chosen among the currently-eligible encounters.</summary>
    [Export] public float SelectionWeight { get; set; } = 1f;

    /// <summary>Chance in 0..1 that each enemy in this encounter rises Ashen (Phase 34F) — the same
    /// archetype, taken by Morthul's corruption. Authored per encounter because in LORE corruption
    /// belongs to the *place*, not the player: "Corrupted forests" in the Ashen Wilds, "Corrupted by
    /// Morthul". Phase 44.5's realm decay tier can drive this later without changing the field.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float CorruptionChance { get; set; } = 0f;

    /// <summary>Regions this encounter may roll in (Phase 34.5B). <b>Empty means anywhere</b>, which is
    /// how every pre-34.5B encounter keeps its old behaviour. Author it when a creature belongs to one
    /// realm — a Frostfang raider has no business patrolling the Ember Crown valley.</summary>
    [Export] public Godot.Collections.Array<string> RegionIds { get; set; } = new();

    [ExportGroup("Allowed Time of Day")]
    [Export] public bool AtDawn { get; set; } = true;
    [Export] public bool AtDay { get; set; } = true;
    [Export] public bool AtDusk { get; set; } = true;
    [Export] public bool AtNight { get; set; } = true;

    /// <summary>Whether this encounter may trigger during the given day phase.</summary>
    public bool AllowedIn(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => AtDawn,
        DayPhase.Day => AtDay,
        DayPhase.Dusk => AtDusk,
        _ => AtNight,
    };

    /// <summary>Whether this encounter may trigger in the given region (empty <see cref="RegionIds"/>
    /// = anywhere).</summary>
    public bool AllowedIn(string regionId) => RegionIds.Count == 0 || RegionIds.Contains(regionId);

    /// <summary>A randomised group size within the authored range.</summary>
    public int RollCount()
    {
        int min = Mathf.Min(MinCount, MaxCount);
        int max = Mathf.Max(MinCount, MaxCount);
        return min + Mathf.FloorToInt(GD.Randf() * (max - min + 1));
    }
}
