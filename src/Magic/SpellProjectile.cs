using System;
using Embervale.Combat;
using Embervale.Entities;
using Embervale.Magic.Vfx;
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

    /// <summary>The bolt's collision radius. Also the longest distance it may travel between two
    /// collision tests — see <see cref="_PhysicsProcess"/>.</summary>
    private const float Radius = 0.25f;

    /// <summary>Ceiling on the sub-steps one physics frame may take, so a spell authored with an
    /// absurd speed (or a frame that hitched for a second) cannot spin here. At the cap the bolt is
    /// travelling faster than the sweep can honestly resolve and the last sub-step's query stands.</summary>
    private const int MaxSubSteps = 16;

    private SpellResource _spell = null!;
    private DamagePacket _packet;
    private IEntity? _caster;
    private int _casterTeam;
    private Node? _casterBody;
    private Vector3 _direction;
    private double _life;
    private bool _resolved = true; // inert until Launch arms it
    private SpellLifetime? _lifetime;
    private bool _cancelled;
    private bool _releaseQueued;
    private bool _returning;

    /// <summary>Foes this bolt may still pass through (a piercing spell), and the ones it has already
    /// struck, so a lance that overlaps a target for several steps hits it once.</summary>
    private int _pierceLeft;
    private readonly HitDedupe _struck = new();

    private const string StormbrandId = "status.stormbrand";

    /// <summary>The reused sweep query. See <see cref="SweepHit"/>.</summary>
    private PhysicsShapeQueryParameters3D? _sweepQuery;
    private PhysicsShapeQueryParameters3D? _homingQuery;
    private SphereShape3D? _homingShape;

    private StandardMaterial3D _material = null!;
    private OmniLight3D _light = null!;

    /// <summary>The plain sphere and light a bolt is drawn as when <see cref="SpellVfx"/> draws
    /// nothing for it. One node, so the whole fallback shows or hides together.</summary>
    private Node3D _plain = null!;

    /// <summary>
    /// Where the bolt's picture should start, when that is not where the bolt itself starts: the
    /// casting hand. Set before <see cref="Launch"/>, which reads it once and clears it; unset, the
    /// picture starts on the bolt. Drawing only: the bolt's path and collision never move.
    /// </summary>
    public Vector3? VisualOrigin { get; set; }

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the projectile frees itself.</summary>
    public Action<SpellProjectile>? Released { get; set; }

    public override void _Ready()
    {
        CollisionLayer = CombatLayers.Hitbox;
        CollisionMask = CombatLayers.ProjectileMask;
        Monitorable = false;
        Monitoring = false;

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
        };
        _plain = new Node3D { Name = "Plain" };
        AddChild(_plain);
        _plain.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f },
            MaterialOverride = _material,
        });

        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.25f } });

        _light = new OmniLight3D { OmniRange = 4f, LightEnergy = 1.2f };
        _plain.AddChild(_light);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime?.Dispose();
            _sweepQuery?.Dispose();
            _homingQuery?.Dispose();
            _homingShape?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>(Re)configures and arms the projectile for a new shot. Call after it is in the
    /// tree and positioned (the visual children must already exist from <see cref="_Ready"/>).</summary>
    public void Launch(SpellResource spell, DamagePacket packet, IEntity? caster, int casterTeam, Vector3 direction)
    {
        _lifetime?.Dispose();
        _cancelled = false;
        _releaseQueued = false;
        _spell = spell;
        _packet = packet;
        _caster = caster;
        _casterTeam = casterTeam;
        _casterBody = caster?.Body;
        _direction = direction.Normalized();
        _life = spell.ProjectileSpeed > 0f ? spell.Range / spell.ProjectileSpeed : 2d;
        _pierceLeft = spell.ImpactRadius > 0f ? 0 : SpellRules.PierceCount(spell.PierceCount, spell.PierceChargeBonus, packet.Charge);
        _struck.Clear();

        Color color = SpellSchools.Color(spell.School);
        _material.AlbedoColor = color;
        _material.Emission = color;
        _light.LightColor = color;

        // The plain sphere stands in whenever the effect layer draws nothing for this bolt.
        Vector3 visualOrigin = VisualOrigin ?? GlobalPosition;
        VisualOrigin = null;
        _plain.Visible = !SpellVfx.AttachProjectile(this, spell, caster, visualOrigin, _direction, packet.Charge);

        _resolved = false;
        CollisionLayer = CombatLayers.Hitbox;
        CollisionMask = CombatLayers.ProjectileMask;
        SetPhysicsProcess(true);
        // Flight uses direct queries; maintaining an unused overlap list adds broadphase work
        // for every bolt in the air and never supplies a more current collision result.
        Monitoring = false;
        _lifetime = new SpellLifetime(this, caster, Cancel);
        _lifetime.Check();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resolved || _cancelled || _lifetime?.Check() != true)
        {
            return;
        }

        // Homing (Phase 29.5G — Ball Lightning): bend toward the nearest hostile each frame.
        if (_spell.HomingRange > 0f && NearestHostile(_spell.HomingRange) is { } target)
        {
            // At the foe's body, not its origin: a hurtbox sits at the feet, and a bolt steered there
            // dives into the floor.
            _direction = SpellHoming.Steer(
                _direction, SpellResolver.VolumeCentre(target) - GlobalPosition, HomingTurnRate, (float)delta);
        }

        // ⚠️ THE FLIGHT IS SWEPT, NOT TELEPORTED. It used to be one `GlobalPosition += v * delta`
        // followed by an overlap test at the arrival point, which is wrong in two compounding ways.
        // A bolt at 40 m/s covers 0.67 m per frame at 60 Hz and more than two metres in one 30 Hz
        // hitch, so a thin target — an actor's hurtbox, a fence, a wall — sitting between the two
        // positions was simply never tested: the shot passed through it and detonated on whatever
        // was behind. And GetOverlappingAreas/Bodies report the state of the PREVIOUS physics step,
        // so even the arrival test was a frame stale. Stepping in Radius-sized increments and asking
        // the space state directly fixes both: no gap is larger than the bolt, and every query is
        // for where the bolt is now.
        float distance = _spell.ProjectileSpeed * (float)delta;
        int steps = SpellSweep.SubStepCount(distance, Radius, MaxSubSteps);
        Vector3 step = _direction * (distance / steps);
        _life -= delta;

        for (int i = 0; i < steps; i++)
        {
            Vector3 before = GlobalPosition;
            GlobalPosition += step;

            // A barrier stands between two steps: the bolt is spent against it, and bursts there.
            if (SpellBarrier.TryIntercept(
                    before, GlobalPosition, _casterTeam, _packet.Amount, _caster, _spell.Id, out Vector3 stoppedAt, Radius))
            {
                GlobalPosition = stoppedAt;
                Resolve(null, SpellImpactKind.Barrier);
                return;
            }

            if (!SweepHit(out Hurtbox? struck))
            {
                continue;
            }

            // A piercing bolt strikes and flies on, once per foe, until it has pierced its fill.
            if (struck != null && _pierceLeft > 0 && _spell.ImpactRadius <= 0f)
            {
                if (_struck.TryHit(struck.OwnerEntity, struck))
                {
                    SpellResolver.HitOne(this, struck, _packet, _spell, _caster, _casterTeam, lifetime: _lifetime);
                    if (_resolved || _lifetime?.Check() != true)
                    {
                        return;
                    }

                    _pierceLeft--;
                }

                continue;
            }

            Resolve(struck, SpellImpactKind.World);
            return;
        }

        if (_life <= 0d)
        {
            Resolve(null, SpellImpactKind.Expired);
        }
    }

    /// <summary>
    /// Does the bolt's volume touch anything at its current position? Answers with the hurtbox it
    /// should damage, or with <c>true</c> and a null hurtbox for world geometry it should burst
    /// against. A hostile hurtbox wins over geometry in the same frame — a bolt that reaches an
    /// enemy standing against a wall hits the enemy.
    /// </summary>
    private bool SweepHit(out Hurtbox? target)
    {
        target = null;

        // Built once and re-aimed per sub-step: a bolt sweeps several times a frame and there may be
        // a dozen in the air, so allocating a query object and a SphereShape3D per test would churn
        // more than the sweep costs.
        _sweepQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = Radius },
            CollideWithAreas = true,
            CollideWithBodies = true,
            CollisionMask = CombatLayers.ProjectileMask,
        };
        _sweepQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition);
        PhysicsShapeQueryParameters3D query = _sweepQuery;

        bool blocked = false;
        foreach (Godot.Collections.Dictionary hit in
                 GetWorld3D().DirectSpaceState.IntersectShape(query, 16))
        {
            if (!hit.TryGetValue("collider", out Variant colliderVar))
            {
                continue;
            }

            GodotObject? collider = colliderVar.AsGodotObject();
            if (collider is Hurtbox hurtbox)
            {
                if (SpellResolver.IsHostileTarget(hurtbox, _caster, _casterTeam))
                {
                    // A foe this bolt already pierced is behind it now: it neither stops nor hits it again.
                    if (hurtbox.OwnerEntity is { } owner && _struck.Has(owner))
                    {
                        continue;
                    }

                    target = hurtbox;
                    return true;
                }

                continue; // the caster's own hurtbox, or an ally's: pass through
            }

            // Actor capsules share the World layer with walls. Only their hurtboxes resolve a
            // spell: an ally's body cannot stop a bolt, and a foe's larger capsule cannot consume
            // it before it reaches that foe's hurtbox. This matches WorldRay and Arrow.
            if (collider is Node3D body && (_casterBody == null || !ReferenceEquals(body, _casterBody)) &&
                EntityNode.FindOwner(body) == null)
            {
                blocked = true;
            }
        }

        return blocked;
    }

    /// <summary>The valid hostile hurtbox a homing bolt hunts within <paramref name="radius"/> of it
    /// (Phase 29.5G): a foe carrying <c>status.stormbrand</c> first, else the nearest. A sphere query on the
    /// Hurtbox layer, mirroring <see cref="SpellResolver.Detonate"/>.</summary>
    private Hurtbox? NearestHostile(float radius)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        _homingShape ??= new SphereShape3D();
        _homingQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = _homingShape,
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };
        _homingShape.Radius = radius;
        _homingQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition);

        Hurtbox? best = null;
        float bestDistance = float.PositiveInfinity;
        bool bestBranded = false;
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(_homingQuery, 16))
        {
            if (hit.TryGetValue("collider", out Variant colliderVar) &&
                colliderVar.AsGodotObject() is Hurtbox hurtbox &&
                SpellResolver.IsHostileTarget(hurtbox, _caster, _casterTeam) &&
                !(hurtbox.OwnerEntity is { } owner && _struck.Has(owner)))
            {
                bool branded = hurtbox.OwnerEntity?.GetComponent<StatusEffectsComponent>()?.Has(StormbrandId) == true;
                float distance = SpellResolver.VolumeCentre(hurtbox).DistanceSquaredTo(GlobalPosition);
                if (best == null || SpellHoming.IsPreferred(distance, branded, bestDistance, bestBranded))
                {
                    best = hurtbox;
                    bestDistance = distance;
                    bestBranded = branded;
                }
            }
        }

        return best;
    }

    /// <summary>Ends the flight. <paramref name="surface"/> says what stopped a bolt that struck no
    /// foe (a wall, a barrier, the end of its range), for the effect it leaves there.</summary>
    private void Resolve(Hurtbox? primary, SpellImpactKind surface)
    {
        if (_resolved || _cancelled || _lifetime?.Check() != true)
        {
            return;
        }

        _resolved = true;
        Monitoring = false;

        // Resolve impact while still in the tree (the detonation queries this node's world).
        if (_spell.ImpactRadius > 0f)
        {
            SpellResolver.Detonate(this, _spell, _packet, _caster, _casterTeam, GlobalPosition, _spell.ImpactRadius, _lifetime);
        }
        else if (primary != null)
        {
            SpellResolver.HitOne(this, primary, _packet, _spell, _caster, _casterTeam, lifetime: _lifetime);
        }
        else
        {
            SpellVfx.Impact(_spell, _caster, new SpellImpactInfo(
                GlobalPosition, -_direction, null, surface, _packet.Charge));
        }

        SpellVfx.DetachProjectile(this);

        // Defer the detach/free: we're inside this node's own physics step, and _resolved keeps
        // it inert until then. (The pool reclaims it; without a pool it frees itself.)
        if (_cancelled || _lifetime?.Check() != true)
        {
            return;
        }

        _releaseQueued = true;
        Callable.From(Release).CallDeferred();
    }

    private void Release()
    {
        if (!IsInstanceValid(this) || !_releaseQueued || _cancelled || _lifetime?.Check() != true)
        {
            return;
        }

        _releaseQueued = false;
        if (Released != null)
        {
            _returning = true;
            try
            {
                Released(this);
            }
            finally
            {
                _returning = false;
            }
        }
        else
        {
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        SpellVfx.DetachProjectile(this);
        VisualOrigin = null;
        _lifetime?.Dispose();
        _resolved = true;
        _cancelled = true;
        _releaseQueued = false;
        _caster = null;
        _casterBody = null;
        _packet = default;
        _struck.Clear();
        if (!_returning)
        {
            Released = null;
        }
    }

    private void Cancel()
    {
        _resolved = true;
        _cancelled = true;
        _releaseQueued = false;
        _caster = null;
        _casterBody = null;
        _packet = default;
        _struck.Clear();
        Released = null; // a cancelled shot is freed, never returned to a possibly torn-down pool
        SpellVfx.DetachProjectile(this);
        CollisionLayer = 0u;
        CollisionMask = 0u;
        Monitoring = false;
        SetProcess(false);
        SetPhysicsProcess(false);
        Hide();
        QueueFree();
    }
}
