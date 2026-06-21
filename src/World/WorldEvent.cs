using System.Collections.Generic;
using Embervale.Enemies;
using Godot;

namespace Embervale.World;

/// <summary>
/// The runtime state of one active <see cref="WorldEventResource"/>: where it spawned, the
/// actors/loot it owns, objective progress, and the countdown to its time limit. The
/// <see cref="WorldEventDirector"/> owns it and advances it; the UI reads it for the HUD.
/// </summary>
public sealed class WorldEvent
{
    public WorldEvent(WorldEventResource resource, Vector3 origin, int required, double timeLimit)
    {
        Resource = resource;
        Origin = origin;
        Required = Mathf.Max(1, required);
        TimeLeft = timeLimit;
    }

    public WorldEventResource Resource { get; }

    public Vector3 Origin { get; }

    public int Required { get; }

    public int Progress { get; set; }

    /// <summary>Seconds remaining; <see cref="double.PositiveInfinity"/> when untimed.</summary>
    public double TimeLeft { get; set; }

    public WorldEventStatus Status { get; set; } = WorldEventStatus.Active;

    /// <summary>Spawned foes (Raid/Hunt) tracked by runtime id for kill credit + cleanup.</summary>
    public HashSet<ulong> EnemyIds { get; } = new();

    public List<EnemyEntity> Enemies { get; } = new();

    public bool IsTimed => !double.IsPositiveInfinity(TimeLeft);

    public bool IsComplete => Progress >= Required;

    /// <summary>A short objective line for the HUD.</summary>
    public string ObjectiveLabel()
    {
        return Resource.Kind switch
        {
            WorldEventKind.Cache => $"Collect the cache ({Progress}/{Required})",
            WorldEventKind.Hunt => $"Slay the champion ({Progress}/{Required})",
            _ => $"Defeat the raiders ({Progress}/{Required})",
        };
    }
}
