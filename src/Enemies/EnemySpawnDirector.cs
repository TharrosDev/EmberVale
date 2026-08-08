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
    [Export] public int MaxAlive { get; set; } = 15;

    /// <summary>⚠️ <see cref="_Ready"/> seeds the whole population in one go, so this has to be wide
    /// enough for <see cref="MaxAlive"/> bodies to land clear of each other — raised with the cap in
    /// 38M2, because fifteen goblins in the old eight-metre circle spawn inside one another.</summary>
    [Export] public float SpawnRadius { get; set; } = 14f;
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
        // Create-at-zero, add, then place in world space — the same order BossSummonComponent uses.
        // The factories take a *local* position, so handing one a world point and then parenting the
        // actor under a node that is not at the origin applies the offset twice. Harmless while this
        // director only ever sits at the world origin; a real bug the moment one is dropped into a
        // streamed region cell, which is exactly how it bit the 35D lair spawner.
        EnemyEntity enemy = EnemyFactory.Create(Vector3.Zero);
        GetParent().AddChild(enemy);
        enemy.GlobalPosition = RandomPoint();
        _alive++;
        enemy.TreeExited += OnEnemyRemoved;
    }

    private void OnEnemyRemoved()
    {
        _alive = Mathf.Max(0, _alive - 1);

        // A death starts the respawn clock fresh, so the replacement always waits the full interval —
        // without this the timer sits at 0 from the initial seed and the first refill pops instantly.
        _respawnTimer = RespawnInterval;
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
