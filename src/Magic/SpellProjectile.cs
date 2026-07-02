using System;
using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A travelling spell bolt. It is the magic analogue of a melee <see cref="Hitbox"/>:
/// an <see cref="Area3D"/> on the Hitbox layer that flies forward each physics frame
/// and resolves the moment it overlaps an enemy hurtbox, hits world geometry, or runs
/// out of range. Resolution delegates to <see cref="SpellResolver"/> — a single-target
/// strike, or an area burst when the spell carries an <see cref="SpellResource.ImpactRadius"/>.
///
/// Projectiles are pooled (Phase 19): the visual + collision children are built once in
/// <see cref="_Ready"/>, each shot reconfigures via <see cref="Launch"/>, and on resolution
/// it invokes <see cref="Released"/> (the pool reclaims it) instead of freeing — so rapid
/// casting doesn't churn the scene tree. With no callback it falls back to freeing itself.
/// </summary>
public partial class SpellProjectile : Area3D
{
    /// <summary>Fraction-per-second a homing bolt turns toward its target (Phase 29.5G).</summary>
    private const float HomingTurnRate = 3.5f;

    private SpellResource _spell = null!;
    private DamagePacket _packet;
    private IEntity? _caster;
    private int _casterTeam;
    private Node? _casterBody;
    private Vector3 _direction;
    private double _life;
    private bool _resolved = true; // inert until Launch arms it

    private StandardMaterial3D _material = null!;
    private OmniLight3D _light = null!;

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the projectile frees itself.</summary>
    public Action<SpellProjectile>? Released { get; set; }

    public override void _Ready()
    {
        CollisionLayer = CombatLayers.Hitbox;
        CollisionMask = CombatLayers.Hurtbox | CombatLayers.World;
        Monitorable = false;
        Monitoring = false;

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f },
            MaterialOverride = _material,
        });

        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.25f } });

        _light = new OmniLight3D { OmniRange = 4f, LightEnergy = 1.2f };
        AddChild(_light);
    }

    /// <summary>(Re)configures and arms the projectile for a new shot. Call after it is in the
    /// tree and positioned (the visual children must already exist from <see cref="_Ready"/>).</summary>
    public void Launch(SpellResource spell, DamagePacket packet, IEntity? caster, int casterTeam, Vector3 direction)
    {
        _spell = spell;
        _packet = packet;
        _caster = caster;
        _casterTeam = casterTeam;
        _casterBody = caster?.Body;
        _direction = direction.Normalized();
        _life = spell.ProjectileSpeed > 0f ? spell.Range / spell.ProjectileSpeed : 2d;

        Color color = SpellSchools.Color(spell.School);
        _material.AlbedoColor = color;
        _material.Emission = color;
        _light.LightColor = color;

        _resolved = false;
        Monitoring = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resolved)
        {
            return;
        }

        // Homing (Phase 29.5G — Ball Lightning): bend toward the nearest hostile each frame.
        if (_spell.HomingRange > 0f && NearestHostile(_spell.HomingRange) is { } target)
        {
            _direction = SpellHoming.Steer(_direction, target.GlobalPosition - GlobalPosition, HomingTurnRate, (float)delta);
        }

        GlobalPosition += _direction * _spell.ProjectileSpeed * (float)delta;
        _life -= delta;

        // Prefer a hurtbox we can actually damage.
        foreach (Area3D area in GetOverlappingAreas())
        {
            if (area is Hurtbox hurtbox && SpellResolver.IsHostileTarget(hurtbox, _caster, _casterTeam))
            {
                Resolve(hurtbox);
                return;
            }
        }

        // Otherwise detonate against world geometry / blocking bodies (but never the caster).
        foreach (Node3D body in GetOverlappingBodies())
        {
            if (_casterBody != null && ReferenceEquals(body, _casterBody))
            {
                continue;
            }

            Resolve(null);
            return;
        }

        if (_life <= 0d)
        {
            Resolve(null);
        }
    }

    /// <summary>The nearest valid hostile hurtbox within <paramref name="radius"/> of the bolt (Phase
    /// 29.5G homing), or null. A sphere query on the Hurtbox layer, mirroring <see cref="SpellResolver.Detonate"/>.</summary>
    private Hurtbox? NearestHostile(float radius)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, GlobalPosition),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        Hurtbox? best = null;
        float bestDist = float.MaxValue;
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 16))
        {
            if (hit.TryGetValue("collider", out Variant colliderVar) &&
                colliderVar.AsGodotObject() is Hurtbox hurtbox &&
                SpellResolver.IsHostileTarget(hurtbox, _caster, _casterTeam))
            {
                float dist = hurtbox.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = hurtbox;
                }
            }
        }

        return best;
    }

    private void Resolve(Hurtbox? primary)
    {
        _resolved = true;
        Monitoring = false;

        // Resolve impact while still in the tree (the detonation queries this node's world).
        if (_spell.ImpactRadius > 0f)
        {
            SpellResolver.Detonate(this, _spell, _packet, _caster, _casterTeam, GlobalPosition, _spell.ImpactRadius);
        }
        else if (primary != null)
        {
            SpellResolver.HitOne(this, primary, _packet, _spell, _caster, _casterTeam);
        }

        // Defer the detach/free: we're inside this node's own physics step, and _resolved keeps
        // it inert until then. (The pool reclaims it; without a pool it frees itself.)
        Callable.From(Release).CallDeferred();
    }

    private void Release()
    {
        if (Released != null)
        {
            Released(this);
        }
        else
        {
            QueueFree();
        }
    }
}
