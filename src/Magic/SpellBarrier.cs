using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A standing wall (magic upgrade 2026-09 — Pyre Wall, Glacial Bulwark): <see cref="Width"/> across the
/// aim, <see cref="Height"/> tall, for <see cref="SpellResource.BarrierDuration"/> seconds.
///
/// <para><b>It stops projectiles by segment test, not by physics.</b> Every spell bolt and arrow asks
/// <see cref="TryIntercept"/> for each step it takes, so the same rule covers a Pyre Wall (which eats
/// arrows and spells but lets bodies walk into the fire) and a Glacial Bulwark (which is also solid to
/// bodies, through a static body on the world layer). A hostile bolt that crosses the wall is consumed,
/// its damage is taken off the wall's health, and <see cref="SpellBlockedEvent"/> says so. A wall's
/// own team fires through a wall that is not solid.</para>
///
/// <para>A wall that is not solid applies the spell's damage and status to whatever stands in it,
/// once on entering and again every zone tick while it stays. A barrier ends with
/// <see cref="BarrierEndedEvent"/>: <c>Broken</c> when its health ran out, false when it expired.</para>
/// </summary>
public partial class SpellBarrier : Node3D
{
    /// <summary>How tall the wall stands, metres.</summary>
    public const float WallHeight = 2.6f;

    private const float Thickness = 0.5f;
    private const double PollSeconds = 0.15d;

    private static readonly List<SpellBarrier> ActiveBarriers = new();

    public SpellResource Spell { get; set; } = null!;
    public DamagePacket Packet { get; set; }
    public IEntity? Caster { get; set; }
    public int CasterTeam { get; set; }
    public float Width { get; set; } = 4f;
    public float Duration { get; set; } = 6f;

    /// <summary>Damage left before the wall breaks; a wall authored with no health cannot break.</summary>
    public float Health { get; private set; }

    public bool Ended { get; private set; }

    /// <summary>The number of barriers standing right now (probes and diagnostics).</summary>
    public static int Count => ActiveBarriers.Count;

    private bool Solid => Spell.BarrierBlocksBodies;

    private StandardMaterial3D? _material;
    private double _age;
    private double _poll;
    private readonly Dictionary<ulong, double> _nextHit = new();
    private readonly HashSet<ulong> _seen = new();

    public override void _EnterTree() => ActiveBarriers.Add(this);

    public override void _ExitTree() => ActiveBarriers.Remove(this);

    public override void _Ready()
    {
        Health = Spell.BarrierHealth;
        Color tint = SpellSchools.Color(Spell.School);
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = 1.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Face",
            Mesh = new BoxMesh { Size = new Vector3(Width, WallHeight, Solid ? 0.35f : 0.18f) },
            Position = new Vector3(0f, WallHeight * 0.5f, 0f),
            MaterialOverride = _material,
        });

        // A base strip so a wall reads on the ground from a distance, solid or not.
        AddChild(new MeshInstance3D
        {
            Name = "Base",
            Mesh = new BoxMesh { Size = new Vector3(Width, 0.06f, Thickness) },
            Position = new Vector3(0f, 0.03f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });

        if (Solid)
        {
            var body = new StaticBody3D { Name = "Solid", CollisionLayer = CombatLayers.WorldStatic, CollisionMask = 0u };
            body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(Width, WallHeight, Thickness) },
                Position = new Vector3(0f, WallHeight * 0.5f, 0f),
            });
            AddChild(body);
        }
    }

    /// <summary>Faces a wall along <paramref name="forward"/>: its face normal is the caster's line
    /// of sight, so it stands square across the aim. Call after it is in the tree.</summary>
    public void Face(Vector3 forward)
    {
        Vector3 f = new Vector3(forward.X, 0f, forward.Z);
        f = f.LengthSquared() < 1e-6f ? Vector3.Forward : f.Normalized();
        Vector3 right = f.Cross(Vector3.Up).Normalized();
        GlobalTransform = new Transform3D(new Basis(right, Vector3.Up, right.Cross(Vector3.Up)), GlobalPosition);
    }

    public override void _Process(double delta)
    {
        if (Ended)
        {
            return;
        }

        _age += delta;
        if (_material != null)
        {
            // Fades as it runs out, so how long is left can be read off the wall.
            float left = Duration > 0f ? Mathf.Clamp(1f - (float)(_age / Duration), 0f, 1f) : 1f;
            Color c = _material.AlbedoColor;
            _material.AlbedoColor = new Color(c.R, c.G, c.B, 0.2f + (0.3f * left));
        }

        if (!Solid)
        {
            _poll -= delta;
            if (_poll <= 0d)
            {
                _poll = PollSeconds;
                BurnWhatStandsInIt();
            }
        }

        if (_age >= Duration)
        {
            End(broken: false);
        }
    }

    /// <summary>Takes damage off the wall. Breaks it when the health is spent.</summary>
    public void Absorb(float amount)
    {
        if (Ended || Spell.BarrierHealth <= 0f)
        {
            return;
        }

        Health -= Mathf.Max(0f, amount);
        if (Health <= 0f)
        {
            End(broken: true);
        }
    }

    public void End(bool broken)
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        EventBus.Instance?.Publish(new BarrierEndedEvent(Caster, Spell.Id, broken));
        if (broken)
        {
            SpellResolver.SpawnFlashAt(this, GlobalPosition + (Vector3.Up * 1.2f), Width * 0.5f, SpellSchools.Color(Spell.School));
        }

        ActiveBarriers.Remove(this);
        QueueFree();
    }

    /// <summary>Whether a segment from <paramref name="from"/> to <paramref name="to"/> crosses this wall's face,
    /// and where.</summary>
    public bool Crosses(Vector3 from, Vector3 to, out Vector3 at)
    {
        at = to;
        Vector3 normal = GlobalTransform.Basis.Z;
        Vector3 right = GlobalTransform.Basis.X;
        Vector3 centre = GlobalPosition;
        float t = SpellRules.PlaneCrossing((from - centre).Dot(normal), (to - centre).Dot(normal));
        if (t < 0f)
        {
            return false;
        }

        Vector3 p = from.Lerp(to, t);
        if (!SpellRules.OnBarrierFace((p - centre).Dot(right), p.Y - centre.Y, Width, WallHeight))
        {
            return false;
        }

        at = p;
        return true;
    }

    /// <summary>
    /// Asks every standing barrier whether a bolt travelling <paramref name="from"/> to <paramref name="to"/>
    /// is stopped. A hostile barrier that blocks projectiles takes the segment, absorbs
    /// <paramref name="damage"/> and raises <see cref="SpellBlockedEvent"/> when <paramref name="spellId"/>
    /// is given (an arrow passes null). Returns true with the point it was stopped at.
    /// </summary>
    public static bool TryIntercept(
        Vector3 from, Vector3 to, int shooterTeam, float damage, IEntity? shooter, string? spellId, out Vector3 at)
    {
        at = to;
        if (ActiveBarriers.Count == 0)
        {
            return false;
        }

        SpellBarrier? nearest = null;
        float best = float.MaxValue;
        foreach (SpellBarrier barrier in ActiveBarriers)
        {
            if (barrier.Ended || !barrier.Spell.BarrierBlocksProjectiles ||
                (barrier.CasterTeam == shooterTeam && !barrier.Solid) ||
                !barrier.Crosses(from, to, out Vector3 hit))
            {
                continue;
            }

            float d = from.DistanceSquaredTo(hit);
            if (d < best)
            {
                best = d;
                nearest = barrier;
                at = hit;
            }
        }

        if (nearest == null)
        {
            return false;
        }

        if (spellId != null && shooter != null)
        {
            EventBus.Instance?.Publish(new SpellBlockedEvent(shooter, spellId, nearest.Caster));
        }

        nearest.Absorb(damage);
        return true;
    }

    /// <summary>A wall that is not solid burns what stands in it: the spell's damage (when it has any) and
    /// status, once on entering and again each zone tick while it stays.</summary>
    private void BurnWhatStandsInIt()
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Width, WallHeight, Thickness + 0.6f) },
            Transform = new Transform3D(GlobalTransform.Basis, GlobalPosition + (Vector3.Up * WallHeight * 0.5f)),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        double now = _age;
        double tick = Mathf.Max(0.2f, Spell.ZoneTickInterval);
        _seen.Clear();
        var struck = new HitDedupe();
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 32))
        {
            if (!hit.TryGetValue("collider", out Variant v) || v.AsGodotObject() is not Hurtbox hurtbox ||
                hurtbox.OwnerEntity is not { } owner ||
                !SpellResolver.IsHostileTarget(hurtbox, Caster, CasterTeam) ||
                !struck.TryHit(owner, hurtbox))
            {
                continue;
            }

            _seen.Add(owner.RuntimeId);
            if (_nextHit.TryGetValue(owner.RuntimeId, out double due) && now < due)
            {
                continue;
            }

            _nextHit[owner.RuntimeId] = now + tick;
            SpellResolver.HitOne(this, hurtbox, Packet, Spell, Caster, CasterTeam, dealDamage: Spell.BaseDamage > 0f);
        }

        // Anyone who stepped out is forgotten, so walking back in burns at once.
        List<ulong>? gone = null;
        foreach (ulong id in _nextHit.Keys)
        {
            if (!_seen.Contains(id))
            {
                (gone ??= new List<ulong>()).Add(id);
            }
        }

        if (gone != null)
        {
            foreach (ulong id in gone)
            {
                _nextHit.Remove(id);
            }
        }
    }
}
