using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Keeps a target population of enemies alive within a radius of its position.
/// It seeds the initial group on ready and, whenever an enemy is removed from the
/// tree (death despawn), schedules a replacement after a delay. Tracking via the
/// node's <c>TreeExited</c> signal keeps the count accurate without bespoke
/// bookkeeping. A lightweight stand-in for the encounter/spawn systems of later
/// phases.
/// </summary>
[GlobalClass]
public partial class EnemySpawnDirector : Node3D
{
    [Export] public int MaxAlive { get; set; } = 3;
    [Export] public float SpawnRadius { get; set; } = 8f;
    [Export] public float RespawnInterval { get; set; } = 5f;

    private int _alive;
    private double _respawnTimer;

    public override void _Ready()
    {
        for (int i = 0; i < MaxAlive; i++)
        {
            SpawnOne();
        }
    }

    public override void _Process(double delta)
    {
        if (_alive >= MaxAlive)
        {
            return;
        }

        _respawnTimer -= delta;
        if (_respawnTimer <= 0d)
        {
            SpawnOne();
            _respawnTimer = RespawnInterval;
        }
    }

    private void SpawnOne()
    {
        EnemyEntity enemy = EnemyFactory.Create(RandomPoint());
        GetParent().AddChild(enemy);
        _alive++;
        enemy.TreeExited += OnEnemyRemoved;
    }

    private void OnEnemyRemoved()
    {
        _alive = Mathf.Max(0, _alive - 1);
    }

    private Vector3 RandomPoint()
    {
        float angle = GD.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(GD.Randf()) * SpawnRadius;
        Vector3 center = GlobalPosition;
        return new Vector3(
            center.X + (Mathf.Cos(angle) * radius),
            center.Y + 0.5f,
            center.Z + (Mathf.Sin(angle) * radius));
    }
}
