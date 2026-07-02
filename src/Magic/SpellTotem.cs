using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A summoned healing totem (Phase 29.5G — Lifebloom Totem): a stationary marker that heals its owner by
/// <see cref="HealPerTick"/> every <see cref="TickInterval"/> for <see cref="Duration"/> seconds, then
/// frees itself. Heal amount is snapshotted at cast time (already empowered).
///
/// ponytail: heals its owner only — no AI, no collision, no nav, no thorns pulse. A proper summon system
/// (companions/minions) is Phase 32; this is the minimum that makes Nature's sustain identity castable.
/// </summary>
public partial class SpellTotem : Node3D
{
    public StatsComponent? Target { get; set; }
    public float HealPerTick { get; set; }
    public float Duration { get; set; } = 6f;
    public float TickInterval { get; set; } = 1f;
    public Color Tint { get; set; } = new(0.40f, 0.85f, 0.45f);

    private double _life;
    private double _tickTimer = 1f; // wait one interval before the first heal

    public override void _Ready()
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.12f, Height = 0.9f },
            Position = new Vector3(0f, 0.45f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Tint,
                EmissionEnabled = true,
                Emission = Tint,
                EmissionEnergyMultiplier = 0.7f,
            },
        };
        AddChild(mesh);
    }

    public override void _Process(double delta)
    {
        _life += delta;
        _tickTimer -= delta;
        if (_tickTimer <= 0d)
        {
            _tickTimer += TickInterval;
            if (Target is { IsAlive: true } stats && IsInstanceValid(stats))
            {
                stats.Heal(HealPerTick);
            }
        }

        if (_life >= Duration)
        {
            QueueFree();
        }
    }
}
