using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic.Vfx;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A summoned healing totem (Phase 29.5G — Lifebloom Totem): a stationary marker that heals its owner by
/// <see cref="HealPerTick"/> every <see cref="TickInterval"/> for <see cref="Duration"/> seconds, then
/// frees itself. Heal amount is snapshotted at cast time (already empowered).
///
/// <para><b>It can be destroyed</b> (magic upgrade 2026-09). It is a small <see cref="Entity"/> on the
/// caster's team with health and a <see cref="Hurtbox"/>, so anything that can hit an actor can hit the
/// totem — a sword, an arrow, a bolt, a blast — and when its health is gone it ends with
/// <see cref="BarrierEndedEvent"/> (<c>Broken</c>) and heals no more. Nothing hunts it on purpose.</para>
/// </summary>
public partial class SpellTotem : Entity
{
    /// <summary>Health a totem has when its spell authors none.</summary>
    public const float DefaultHealth = 40f;

    public StatsComponent? Target { get; set; }
    public float HealPerTick { get; set; }
    public float Duration { get; set; } = 6f;
    public float TickInterval { get; set; } = 1f;
    public Color Tint { get; set; } = new(0.40f, 0.85f, 0.45f);

    /// <summary>The spell that raised it and who cast it (for the end event).</summary>
    public SpellResource? Spell { get; set; }
    public IEntity? Caster { get; set; }
    public int CasterTeam { get; set; }
    public float Health { get; set; } = DefaultHealth;

    public bool Ended { get; private set; }

    private StatsComponent? _stats;
    private Hurtbox? _hurtbox;
    private SpellLifetime? _lifetime;
    private double _life;
    private double _tickTimer = 1f; // wait one interval before the first heal

    public override void _Ready()
    {
        _lifetime = new SpellLifetime(this, Caster, Cancel);
        base._Ready();
        if (!_lifetime.Check())
        {
            return;
        }

        DisplayName = Spell?.DisplayName ?? "Totem";
        TemplateId = "spell.totem";

        // Health comes from the stats component the totem carries; the components are built before the
        // node is in the tree so they initialise with it.
        var stats = new StatsComponent { Name = "Stats" };
        AddChild(stats);
        AddChild(new CombatComponent { Name = "Combat", Team = CasterTeam });
        _stats = stats;
        stats.SetCurrent(StatType.Health, Mathf.Min(Health, stats.GetMax(StatType.Health)));

        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.0f },
            Position = new Vector3(0f, 0.5f, 0f),
        });
        AddChild(hurtbox);
        _hurtbox = hurtbox;

        // The plain post stands in whenever the effect layer draws nothing for this totem.
        if (!SpellVfx.AttachTotem(this, Spell, Caster, Tint))
        {
            AddChild(new MeshInstance3D
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
            });
        }
    }

    public override void _Process(double delta)
    {
        if (Ended || _lifetime?.Check() != true)
        {
            return;
        }

        if (_stats is { IsAlive: false } && _life > 0d)
        {
            End(broken: true);
            return;
        }

        _life += delta;
        _tickTimer -= delta;
        if (_tickTimer <= 0d)
        {
            _tickTimer += TickInterval;
            if (Target is { } stats && IsInstanceValid(stats) && stats.IsAlive)
            {
                stats.Heal(HealPerTick);
                SpellVfx.TotemPulse(this, Spell, stats.Entity?.Body);
            }
        }

        if (_life >= Duration)
        {
            End(broken: false);
        }
    }

    /// <summary>Damage left, for a probe or a nameplate.</summary>
    public float HealthNow => _stats?.GetCurrent(StatType.Health) ?? Health;

    private void End(bool broken)
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        StopCollision();
        _lifetime?.Dispose();
        SpellVfx.TotemEnd(this, Spell, broken);
        if (Spell != null)
        {
            EventBus.Instance?.Publish(new BarrierEndedEvent(Caster, Spell.Id, broken));
        }

        Target = null;
        Caster = null;
        QueueFree();
    }

    public override void _ExitTree()
    {
        // Freed without ending (its host went away): the effect layer is still told, once more at worst.
        SpellVfx.TotemEnd(this, Spell, broken: false);
        _lifetime?.Dispose();
        Ended = true;
        Target = null;
        Caster = null;
        base._ExitTree();
    }

    private void Cancel()
    {
        Ended = true;
        Target = null;
        Caster = null;
        StopCollision();
        SetProcess(false);
        SetPhysicsProcess(false);
        SpellVfx.TotemEnd(this, Spell, broken: false);
        Hide();
        QueueFree();
    }

    private void StopCollision()
    {
        if (_hurtbox != null && IsInstanceValid(_hurtbox))
        {
            _hurtbox.CollisionLayer = 0u;
            _hurtbox.CollisionMask = 0u;
            _hurtbox.Monitorable = false;
        }
    }
}
