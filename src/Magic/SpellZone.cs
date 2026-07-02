using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A lingering ground zone (Phase 29.5G — Blizzard): re-detonates a spell at its position every
/// <see cref="TickInterval"/> for <see cref="Duration"/> seconds, then frees itself. Each pulse reuses
/// <see cref="SpellResolver.Detonate"/> (damage + the spell's status + a flash), so a Frost zone chills
/// everything inside on a cadence. The <see cref="Packet"/> is snapshotted at cast time.
///
/// ponytail: spawns at the caster (a lingering nova) with a fixed radius; aim-placed zones / growth are a
/// later upgrade. Stops early if its world goes away.
/// </summary>
public partial class SpellZone : Node3D
{
    public SpellResource Spell { get; set; } = null!;
    public DamagePacket Packet { get; set; }
    public IEntity? Caster { get; set; }
    public int CasterTeam { get; set; }
    public float Radius { get; set; } = 4f;
    public float Duration { get; set; } = 4f;
    public float TickInterval { get; set; } = 1f;

    private double _life;
    private double _tickTimer; // 0 → first pulse fires immediately

    public override void _Process(double delta)
    {
        _life += delta;
        _tickTimer -= delta;
        if (_tickTimer <= 0d)
        {
            _tickTimer += TickInterval;
            SpellResolver.Detonate(this, Spell, Packet, Caster, CasterTeam, GlobalPosition, Radius);
        }

        if (_life >= Duration)
        {
            QueueFree();
        }
    }
}
