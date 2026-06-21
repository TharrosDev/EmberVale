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

    /// <summary>A randomised group size within the authored range.</summary>
    public int RollCount()
    {
        int min = Mathf.Min(MinCount, MaxCount);
        int max = Mathf.Max(MinCount, MaxCount);
        return min + Mathf.FloorToInt(GD.Randf() * (max - min + 1));
    }
}
