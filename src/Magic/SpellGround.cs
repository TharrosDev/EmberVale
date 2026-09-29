using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A ground spell waiting to land (magic upgrade 2026-09 — Sunfall, Gravity Well, Thornsnare). It sits at
/// the placed point drawing a <see cref="TelegraphRing"/> — the same ring an enemy's wind-up draws — that
/// grows for exactly <see cref="Delay"/> seconds, and when the delay is up the spell lands: a burst, or a
/// <see cref="SpellZone"/> when the spell lingers. A spell with <see cref="SpellResource.PullStrength"/>
/// and no zone keeps drawing foes toward the centre for a moment after it lands.
/// </summary>
public partial class SpellGround : Node3D
{
    private const float DefaultRadius = 3f;
    private const float DefaultPullSeconds = 0.6f;

    public SpellResource Spell { get; set; } = null!;
    public DamagePacket Packet { get; set; }
    public IEntity? Caster { get; set; }
    public int CasterTeam { get; set; }

    /// <summary>Seconds of warning; the ring runs for exactly this long.</summary>
    public float Delay { get; set; }

    public float Radius { get; set; } = DefaultRadius;

    /// <summary>The ring, so a probe can read that it is armed for the delay.</summary>
    public TelegraphRing Ring { get; } = new() { Name = "GroundRing" };

    /// <summary>True from the moment the spell has landed.</summary>
    public bool Landed { get; private set; }

    private double _age;
    private double _pullLeft;

    public override void _Ready()
    {
        AddChild(Ring);
        Ring.Position = new Vector3(0f, 0.06f, 0f);
        TelegraphClass cls = Spell.Blockable ? TelegraphClass.Standard : TelegraphClass.Unblockable;
        Ring.Arm(Delay, Radius, SpellSchools.Color(Spell.School), cls);
    }

    public override void _Process(double delta)
    {
        if (Landed)
        {
            return;
        }

        _age += delta;
        if (_age >= Delay)
        {
            Land();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Landed || _pullLeft <= 0d)
        {
            return;
        }

        _pullLeft -= delta;
        SpellResolver.Pull(this, GlobalPosition, Radius, Spell.PullStrength, delta, Caster, CasterTeam);
        if (_pullLeft <= 0d)
        {
            QueueFree();
        }
    }

    private void Land()
    {
        Landed = true;
        Ring.Clear();
        Vector3 centre = GlobalPosition + (Vector3.Up * 0.3f);

        if (Spell.ZoneDuration > 0f)
        {
            var zone = new SpellZone
            {
                Spell = Spell,
                Packet = Packet,
                Caster = Caster,
                CasterTeam = CasterTeam,
                Radius = Radius,
                Duration = Spell.ZoneDuration,
                TickInterval = Spell.ZoneTickInterval,
                PullStrength = Spell.PullStrength,
            };
            GetTree().CurrentScene.AddChild(zone);
            zone.GlobalPosition = GlobalPosition + (Vector3.Up * 0.1f);
            QueueFree();
            return;
        }

        SpellResolver.Detonate(this, Spell, Packet, Caster, CasterTeam, centre, Radius);
        if (Spell.PullStrength > 0f)
        {
            _pullLeft = Spell.PullSeconds > 0f ? Spell.PullSeconds : DefaultPullSeconds;
            return;
        }

        QueueFree();
    }
}
