using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Spawns dynamic enemy encounters around the player on a cadence that the world bends:
/// encounters come more often at night and during storms. On each check it filters the
/// <see cref="EncounterDatabase"/> by the current <see cref="DayPhase"/>, picks one by
/// weight, and spawns its group on a ring at a distance from the player (out of the
/// immediate area), capped by a concurrent-encounter budget so the world never floods.
///
/// Reuses <see cref="EnemyFactory"/> and the same death/despawn flow as the static camp;
/// it tracks its spawns via <c>TreeExited</c> to keep the live count honest. Emergent and
/// transient, so (like <see cref="Enemies.EnemySpawnDirector"/>) it is not persisted.
/// </summary>
[GlobalClass]
public partial class EncounterDirector : Node3D
{
    /// <summary>Average real seconds between encounter checks during the day.</summary>
    [Export] public float BaseIntervalSeconds { get; set; } = 35f;

    /// <summary>Max enemies alive from encounters at once (the spawn budget).</summary>
    [Export] public int MaxConcurrent { get; set; } = 5;

    [Export] public float SpawnDistanceMin { get; set; } = 14f;
    [Export] public float SpawnDistanceMax { get; set; } = 20f;

    /// <summary>Interval multiplier at night (&lt; 1 = more frequent).</summary>
    [Export] public float NightFrequencyScale { get; set; } = 0.5f;

    /// <summary>Interval multiplier during storms (&lt; 1 = more frequent).</summary>
    [Export] public float StormFrequencyScale { get; set; } = 0.6f;

    private double _timer;
    private int _alive;
    private readonly List<Node3D> _spawns = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _timer = NextInterval();
        EventBus.Instance?.Subscribe<RegionTransitionRequestedEvent>(OnRegionTransition);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionTransitionRequestedEvent>(OnRegionTransition);
    }

    /// <summary>Encounter spawns are parented to the persistent world root, not the streamed cells, so a
    /// region transition would orphan them in the new region. Free them on the boundary; <c>_alive</c>
    /// self-heals through the same <c>TreeExited</c> path each free fires.</summary>
    private void OnRegionTransition(RegionTransitionRequestedEvent e)
    {
        foreach (Node3D spawn in _spawns.ToArray())
        {
            if (IsInstanceValid(spawn))
            {
                spawn.QueueFree();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (GameManager.Instance is { IsPlaying: false })
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0d)
        {
            return;
        }

        _timer = NextInterval();
        TryTrigger();
    }

    private void TryTrigger()
    {
        if (_alive >= MaxConcurrent || EncounterDatabase.All.Count == 0)
        {
            return;
        }

        if (ServiceLocator.Instance == null ||
            !ServiceLocator.Instance.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player))
        {
            return;
        }

        DayPhase phase = CurrentPhase();
        EncounterResource? encounter = PickEligible(phase);
        if (encounter == null)
        {
            return;
        }

        int count = Mathf.Min(encounter.RollCount(), MaxConcurrent - _alive);
        if (count <= 0)
        {
            return;
        }

        Vector3 origin = RingPointAround(player.GlobalPosition);
        for (int i = 0; i < count; i++)
        {
            Vector3 jitter = new(GD.Randf() * 2f - 1f, 0f, GD.Randf() * 2f - 1f);
            SpawnEnemy(encounter.EnemyTemplateId, origin + jitter);
        }

        EventBus.Instance?.Publish(new EncounterTriggeredEvent(encounter.Id, origin, count));
        Log.Info($"Encounter: {encounter.DisplayName} ({count}) appeared near the player.");
    }

    private void SpawnEnemy(string templateId, Vector3 position)
    {
        EnemyEntity enemy = EnemyTemplateRegistry.Create(templateId, position);
        GetParent().AddChild(enemy);
        _alive++;
        _spawns.Add(enemy);
        enemy.TreeExited += () => OnEnemyRemoved(enemy);
    }

    private void OnEnemyRemoved(Node3D enemy)
    {
        _spawns.Remove(enemy);
        _alive = Mathf.Max(0, _alive - 1);
    }

    private static EncounterResource? PickEligible(DayPhase phase)
    {
        var pool = new List<EncounterResource>();
        float total = 0f;
        foreach (EncounterResource e in EncounterDatabase.All)
        {
            if (!e.AllowedIn(phase))
            {
                continue;
            }

            pool.Add(e);
            total += Mathf.Max(0f, e.SelectionWeight);
        }

        if (pool.Count == 0 || total <= 0f)
        {
            return null;
        }

        float roll = GD.Randf() * total;
        foreach (EncounterResource e in pool)
        {
            roll -= Mathf.Max(0f, e.SelectionWeight);
            if (roll <= 0f)
            {
                return e;
            }
        }

        return pool[pool.Count - 1];
    }

    private Vector3 RingPointAround(Vector3 center)
    {
        float angle = GD.Randf() * Mathf.Tau;
        float distance = Mathf.Lerp(SpawnDistanceMin, SpawnDistanceMax, GD.Randf());
        return new Vector3(
            center.X + (Mathf.Cos(angle) * distance),
            0.5f,
            center.Z + (Mathf.Sin(angle) * distance));
    }

    private double NextInterval()
    {
        float scale = 1f;
        if (CurrentPhase() == DayPhase.Night)
        {
            scale *= NightFrequencyScale;
        }

        if (ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out WeatherDirector weather) &&
            weather.CurrentType == WeatherType.Storm)
        {
            scale *= StormFrequencyScale;
        }

        float jitter = 0.7f + (GD.Randf() * 0.6f); // 0.7..1.3
        return Mathf.Max(3f, BaseIntervalSeconds * scale * jitter);
    }

    private static DayPhase CurrentPhase()
    {
        return ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out WorldClock clock)
            ? clock.Phase
            : DayPhase.Day;
    }
}
