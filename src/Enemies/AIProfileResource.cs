using Godot;

namespace Embervale.Enemies;

/// <summary>
/// How an archetype fights, as authored data (Phase 34A). Every tuning knob
/// <see cref="EnemyAIComponent"/> used to export now lives here, so "ranged", "shielded",
/// "pack-flanker", "coward" and "ambusher" are <em>numbers on a resource</em> rather than one-off
/// subclasses of the brain. A new enemy personality is a new <c>.tres</c> under
/// <c>data/ai_profiles/</c> plus a <c>ProfileId</c> on the factory — no code.
///
/// The behaviours are deliberately orthogonal: a profile can be a shielded pack-flanker that kites,
/// because each knob gates one branch independently. A zeroed knob turns its behaviour off, which is
/// what makes <c>ai.brute</c> (all the exotic knobs at 0) identical to the pre-34A goblin.
/// </summary>
[GlobalClass]
public partial class AIProfileResource : Resource
{
    /// <summary>Stable id, e.g. <c>ai.brute</c>. Indexed by <see cref="AIProfileDatabase"/>.</summary>
    [Export] public string Id { get; set; } = "ai.unknown";

    [ExportGroup("Perception")]
    [Export] public float VisionRange { get; set; } = 18f;
    [Export] public float FovDegrees { get; set; } = 110f;

    /// <summary>Inside this radius the target is sensed regardless of facing.</summary>
    [Export] public float ProximityRange { get; set; } = 3f;

    /// <summary>How far this actor's alert carries to allies (0 = it fights silently).</summary>
    [Export] public float AlertRadius { get; set; } = 14f;

    /// <summary>Seconds between line-of-sight raycasts; perception is cached in between.</summary>
    [Export] public float PerceptionInterval { get; set; } = 0.15f;

    [ExportGroup("Idle & patrol")]
    [Export] public float PatrolRadius { get; set; } = 6f;
    [Export] public float IdleDuration { get; set; } = 2.5f;
    [Export] public float InvestigateDuration { get; set; } = 6f;

    [ExportGroup("Melee")]
    /// <summary>Weapon reach — inside this it swings instead of closing.</summary>
    [Export] public float AttackRange { get; set; } = 2.1f;

    /// <summary>Degrees each successive pack member fans off the straight approach line, so a group
    /// surrounds its target instead of queueing up behind one another. 0 = charge straight in.</summary>
    [Export] public float FlankSpreadDegrees { get; set; } = 0f;

    [ExportGroup("Standoff (ranged / caster)")]
    /// <summary>Max range it will fight from. Above <see cref="AttackRange"/> this actor holds a band
    /// and kites rather than charging — the shape shared by casters and (future) archers.</summary>
    [Export] public float StandoffRange { get; set; } = 0f;

    /// <summary>Inside this the actor backs away while still attacking.</summary>
    [Export] public float KiteDistance { get; set; } = 6f;

    /// <summary>Radius a support caster scans for a wounded ally to heal/buff.</summary>
    [Export] public float AllySupportRange { get; set; } = 12f;

    /// <summary>Heal an ally (or itself) whose health falls below this fraction.</summary>
    [Export] public float AllyHealThreshold { get; set; } = 0.6f;

    [ExportGroup("Guard (shielded)")]
    /// <summary>Seconds the guard stays up per cycle. 0 = this actor never blocks.</summary>
    [Export] public float BlockDuration { get; set; } = 0f;

    /// <summary>Seconds the guard stays down between blocks — the window it attacks in.</summary>
    [Export] public float BlockRecovery { get; set; } = 1.6f;

    [ExportGroup("Ambush")]
    /// <summary>Above 0 this actor lies in wait: it never patrols, ignores allies' alerts, and stays
    /// put until the target closes inside this range — then springs.</summary>
    [Export] public float AmbushRange { get; set; } = 0f;

    [ExportGroup("Nerve")]
    /// <summary>Break off below this fraction of health. 0 = fights to the death.</summary>
    [Export] public float RetreatHealthFraction { get; set; } = 0.25f;

    [Export] public float MaxRetreatTime { get; set; } = 3.5f;

    /// <summary>A coward: runs on sight and never willingly closes. Retreat becomes its answer to
    /// spotting the target rather than a wounded-animal reflex.</summary>
    [Export] public bool FleeOnSight { get; set; } = false;

    /// <summary>Seconds a struck actor keeps hunting before forgetting (so it stands down once no
    /// longer hostile by reputation). Refreshed while actually in combat.</summary>
    [Export] public float ProvokeMemory { get; set; } = 12f;

    [ExportGroup("Level of detail")]
    /// <summary>Beyond this distance from the player the AI ticks rarely (and casts no shadow).</summary>
    [Export] public float ActiveDistance { get; set; } = 45f;

    /// <summary>Seconds between ticks while sleeping (far from the player).</summary>
    [Export] public float SleepInterval { get; set; } = 0.5f;

    [Export] public float DespawnDelay { get; set; } = 4f;

    /// <summary>Whether this profile fights from a standoff band rather than closing to melee.</summary>
    public bool IsStandoff => StandoffRange > AttackRange;

    /// <summary>Whether this profile raises a guard between swings.</summary>
    public bool IsShielded => BlockDuration > 0f;

    /// <summary>Whether this profile lies in wait instead of patrolling.</summary>
    public bool IsAmbusher => AmbushRange > 0f;

    /// <summary>The stand-in used when a profile id is missing or unresolvable, so a content typo
    /// degrades to "fights like a brute" rather than a null brain.</summary>
    public static AIProfileResource CreateDefault() => new() { Id = "ai.brute" };
}
