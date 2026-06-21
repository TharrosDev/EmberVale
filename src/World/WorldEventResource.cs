using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored world event: a named, announced happening with an objective, a
/// time limit and rewards. Authored as a <c>.tres</c> under <c>data/world_events/</c> and
/// indexed by <see cref="WorldEventDatabase"/>; the <see cref="WorldEventDirector"/> rolls
/// one onto the world and runs its lifecycle. A new event is a new resource, no code.
///
/// This is the richer layer above the lightweight ambient <see cref="EncounterResource"/>:
/// events are discrete, tracked and rewarded, where encounters are an unannounced trickle.
/// </summary>
[GlobalClass]
public partial class WorldEventResource : Resource
{
    [Export] public string Id { get; set; } = "event.unknown";

    [Export] public string DisplayName { get; set; } = "World Event";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Export] public WorldEventKind Kind { get; set; } = WorldEventKind.Raid;

    [Export] public float SelectionWeight { get; set; } = 1f;

    /// <summary>Real seconds this event stays on cooldown after it ends, before recurring.</summary>
    [Export] public float CooldownSeconds { get; set; } = 120f;

    /// <summary>Seconds to resolve the objective before the event fails (0 = no limit).</summary>
    [Export] public float TimeLimitSeconds { get; set; } = 90f;

    [ExportGroup("Allowed Time of Day")]
    [Export] public bool AtDawn { get; set; } = true;
    [Export] public bool AtDay { get; set; } = true;
    [Export] public bool AtDusk { get; set; } = true;
    [Export] public bool AtNight { get; set; } = true;

    [ExportGroup("Spawn")]
    [Export] public float SpawnDistanceMin { get; set; } = 12f;
    [Export] public float SpawnDistanceMax { get; set; } = 18f;

    /// <summary>Enemy archetype for Raid/Hunt (currently routed through the goblin factory).</summary>
    [Export] public string EnemyTemplateId { get; set; } = "enemy.goblin";

    [Export] public int MinCount { get; set; } = 3;
    [Export] public int MaxCount { get; set; } = 5;

    /// <summary>Max-health multiplier applied to spawned foes (a Hunt champion uses &gt; 1).</summary>
    [Export] public float HealthMultiplier { get; set; } = 1f;

    /// <summary>Item id + quantity for a Cache event (spawned to collect).</summary>
    [Export] public string CacheItemId { get; set; } = string.Empty;
    [Export] public int CacheQuantity { get; set; } = 1;

    [ExportGroup("Reward")]
    [Export] public int XpReward { get; set; } = 0;
    [Export] public int GoldReward { get; set; } = 0;
    [Export] public string RewardItemId { get; set; } = string.Empty;
    [Export] public int RewardItemQuantity { get; set; } = 1;
    [Export] public string FactionRewardId { get; set; } = string.Empty;
    [Export] public int FactionRewardAmount { get; set; } = 0;

    public bool AllowedIn(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => AtDawn,
        DayPhase.Day => AtDay,
        DayPhase.Dusk => AtDusk,
        _ => AtNight,
    };

    public int RollCount()
    {
        int min = Mathf.Min(MinCount, MaxCount);
        int max = Mathf.Max(MinCount, MaxCount);
        return min + Mathf.FloorToInt(GD.Randf() * (max - min + 1));
    }
}
