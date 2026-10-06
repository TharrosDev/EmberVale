using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxBurst"/> is asked to throw.</summary>
internal struct VfxBurstSpec
{
    public Vector3 Position;

    public VfxSchoolColors Colors;

    /// <summary>Overrides the colour the preset would take from <see cref="Colors"/>. Alpha 0 = unset.</summary>
    public Color Tint { get; set; }

    /// <summary>Amount against the preset's own (a plan's density, which carries the tier).</summary>
    public float Density;

    /// <summary>Half size of the box the particles are born in.</summary>
    public Vector3 Extents;

    /// <summary>The way they are thrown. Zero = up.</summary>
    public Vector3 Direction;

    /// <summary>Degrees either side of <see cref="Direction"/>; 180 throws in every direction.</summary>
    public float Spread;

    /// <summary>How flat the spread is (0 = a cone, 1 = a fan in the plane across the emitter's Y):
    /// mist rolling out along the floor.</summary>
    public float Flatness;

    /// <summary>Launch speed against the preset's.</summary>
    public float SpeedScale;

    /// <summary>Fastest launch speed in metres a second, replacing the preset's. 0 = unset.</summary>
    public float Speed;

    /// <summary>Particle size against the preset's.</summary>
    public float SizeScale;

    /// <summary>Lifetime against the preset's.</summary>
    public float LifeScale;

    /// <summary>Gravity against the preset's (0 hangs in the air).</summary>
    public float GravityScale;

    /// <summary>Replaces the preset's damping. Negative = unset.</summary>
    public float Damping;

    /// <summary>The particles are born spread over <see cref="Extents"/> and fall into the centre.</summary>
    public bool Inward;

    /// <summary>A steady stream until stopped, instead of one burst.</summary>
    public bool Continuous;

    /// <summary>A continuous stream stops itself after this many seconds (a column of smoke over a
    /// crater), so nothing has to remember to stop it. 0 = it runs until stopped.</summary>
    public float StreamSeconds;

    public static VfxBurstSpec At(Vector3 position, in VfxSchoolColors colors, float density) => new()
    {
        Position = position,
        Colors = colors,
        Density = density,
        Extents = new Vector3(0.1f, 0.1f, 0.1f),
        Spread = 180f,
        SpeedScale = 1f,
        SizeScale = 1f,
        LifeScale = 1f,
        GravityScale = 1f,
        Damping = -1f,
    };
}

/// <summary>
/// A pooled GPU particle emitter for one preset (<see cref="VfxBurstPresets"/>): one of the seven a
/// recipe names (<see cref="VfxParticles"/>) or one of the extra emitters (<see cref="VfxEmitter"/>).
/// The pool is per preset and an emitter is never reshaped into another: everything set per use is
/// a plain number on the process material (extents, speeds, sizes), never a feature, so reuse
/// compiles nothing.
///
/// <para>Density is <c>AmountRatio</c> over a fixed allocated amount, which is how the tier's
/// particle multiplier is applied without reallocating the emitter.</para>
///
/// <para>Particles live in world space, so an emitter that follows a bolt leaves a trail behind it,
/// and an emitter that is stopped stays where it is until its last particle has died.</para>
/// </summary>
public partial class VfxBurst : VfxEffect
{
    /// <summary>What cold smoke looks like, before a hint of the school is mixed in.</summary>
    private static readonly Color SmokeColour = new(0.085f, 0.08f, 0.078f);

    private readonly VfxBurstPreset _preset;
    private readonly GpuParticles3D _particles;
    private readonly ParticleProcessMaterial _process;
    private readonly ShaderMaterial _draw;
    private readonly VfxRamp _ramp;

    private float _life;
    private bool _continuous;
    private float _streamSeconds;

    /// <summary>The engine may build a script instance with no arguments; a pool never does.</summary>
    public VfxBurst()
        : this((int)VfxParticles.Sparks)
    {
    }

    public VfxBurst(VfxParticles kind)
        : this((int)kind)
    {
    }

    public VfxBurst(VfxEmitter kind)
        : this((int)kind)
    {
    }

    private VfxBurst(int slot)
    {
        Slot = slot;
        _preset = VfxBurstPresets.ForSlot(slot);
        _life = _preset.Life;
        _ramp = _preset.Ramp == VfxRamp.Hot && _preset.Occlude ? VfxRamp.Smoke : _preset.Ramp;

        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.1f, 0.1f, 0.1f),
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = _preset.SpeedMin,
            InitialVelocityMax = _preset.SpeedMax,
            Gravity = new Vector3(0f, _preset.Gravity, 0f),
            DampingMin = _preset.Damping,
            DampingMax = _preset.Damping,
            ScaleMin = _preset.SizeMin,
            ScaleMax = _preset.SizeMax,
            ScaleCurve = SizeOverLife(_preset.Grow, _ramp),
            ColorRamp = ColourOverLife(_ramp),
            ParticleFlagAlignY = _preset.AlignVelocity,
            LifetimeRandomness = 0.35f,
        };

        if (!_preset.AlignVelocity)
        {
            _process.AngleMin = -180f;
            _process.AngleMax = 180f;
        }

        if (_preset.Spin)
        {
            // A puff turns slowly; a shard or a chunk tumbles.
            float spin = _preset.Sprite == VfxSprite.Puff ? 40f : 360f;
            _process.AngularVelocityMin = -spin;
            _process.AngularVelocityMax = spin;
        }

        if (_preset.Turbulence)
        {
            _process.TurbulenceEnabled = true;
            _process.TurbulenceNoiseStrength = 1.4f;
            _process.TurbulenceNoiseScale = 2.5f;
            _process.TurbulenceInfluenceMin = 0.08f;
            _process.TurbulenceInfluenceMax = 0.22f;
        }

        _draw = VfxMaterials.Sprite(VfxTextures.Sprite(_preset.Sprite), _preset.AlignVelocity, _preset.Occlude);
        _draw.SetShaderParameter(VfxMaterials.Dissolve, _preset.Dissolve);
        _draw.SetShaderParameter(VfxMaterials.UseHeat, _ramp == VfxRamp.Heat ? 1f : 0f);
        _draw.SetShaderParameter(VfxMaterials.OccludeCool, _preset.CoolOcclude);

        _particles = new GpuParticles3D
        {
            Name = "Particles",
            Amount = VfxBurstPresets.Allocated(_preset),
            Lifetime = _preset.Life,
            OneShot = true,
            Emitting = false,
            Explosiveness = _preset.Explosiveness,
            Randomness = 0.4f,
            LocalCoords = false,
            ProcessMaterial = _process,
            DrawPass1 = _preset.AlignVelocity ? VfxMaterials.StreakQuad : VfxMaterials.Quad,
            MaterialOverride = _draw,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        VfxMaterials.OnLayer(_particles);
        AddChild(_particles);
    }

    /// <summary>The slot of the pool table this emitter was built for: a <see cref="VfxParticles"/>
    /// or a <see cref="VfxEmitter"/> number.</summary>
    public int Slot { get; }

    /// <summary>The recipe preset this emitter was built for, or None for an extra emitter.</summary>
    public VfxParticles Kind => Slot < (int)VfxEmitter.Flame ? (VfxParticles)Slot : VfxParticles.None;

    /// <summary>Styles and fires the emitter. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxBurstSpec spec)
    {
        GlobalPosition = spec.Position;
        _continuous = spec.Continuous;
        _streamSeconds = spec.Continuous ? Mathf.Max(0f, spec.StreamSeconds) : 0f;

        float speedScale = spec.SpeedScale <= 0f ? 1f : spec.SpeedScale;
        float sizeScale = spec.SizeScale <= 0f ? 1f : spec.SizeScale;
        float lifeScale = spec.LifeScale <= 0f ? 1f : spec.LifeScale;
        _life = Mathf.Max(0.05f, _preset.Life * lifeScale);

        float speedMax = spec.Speed > 0f ? spec.Speed : _preset.SpeedMax * speedScale;
        float speedMin = spec.Speed > 0f ? spec.Speed * 0.55f : _preset.SpeedMin * speedScale;
        Vector3 extents = new(
            Mathf.Max(0.005f, spec.Extents.X), Mathf.Max(0.005f, spec.Extents.Y), Mathf.Max(0.005f, spec.Extents.Z));

        _process.EmissionBoxExtents = extents;
        _process.Direction = spec.Direction.LengthSquared() < 0.0001f ? Vector3.Up : spec.Direction.Normalized();
        _process.Spread = Mathf.Clamp(spec.Spread, 0f, 180f);
        _process.Flatness = Mathf.Clamp(spec.Flatness, 0f, 1f);
        _process.ScaleMin = _preset.SizeMin * sizeScale;
        _process.ScaleMax = _preset.SizeMax * sizeScale;
        _process.Gravity = new Vector3(0f, _preset.Gravity * spec.GravityScale, 0f);
        float damping = spec.Damping >= 0f ? spec.Damping : _preset.Damping;
        _process.DampingMin = damping;
        _process.DampingMax = damping;

        float reach;
        if (spec.Inward)
        {
            // Born across the box, drawn to the centre: an acceleration that covers the box in a life.
            float radius = Mathf.Max(extents.X, Mathf.Max(extents.Y, extents.Z));
            float pull = 2f * radius / (_life * _life);
            _process.InitialVelocityMin = 0f;
            _process.InitialVelocityMax = speedMin * 0.15f;
            _process.RadialAccelMin = -pull * 1.2f;
            _process.RadialAccelMax = -pull * 0.8f;
            reach = radius;
        }
        else
        {
            _process.InitialVelocityMin = speedMin;
            _process.InitialVelocityMax = speedMax;
            _process.RadialAccelMin = 0f;
            _process.RadialAccelMax = 0f;
            reach = Mathf.Max(extents.X, Mathf.Max(extents.Y, extents.Z)) + (speedMax * _life);
        }

        VfxSchoolColors colors = spec.Colors;
        Color tint;
        float energy;
        if (_preset.Occlude)
        {
            // Smoke and debris are dark and only hinted with the school; lit by nothing, they never bloom.
            tint = SmokeColour.Lerp(colors.Edge, 0.16f);
            energy = 1f;
        }
        else
        {
            tint = _preset.AlignVelocity || _ramp is VfxRamp.Twinkle or VfxRamp.Soft ? colors.Core : colors.Mid;
            energy = colors.MidEnergy * _preset.Energy;
        }

        if (spec.Tint.A > 0f)
        {
            tint = new Color(spec.Tint.R, spec.Tint.G, spec.Tint.B);
        }

        _draw.SetShaderParameter(VfxMaterials.Tint, tint);
        _draw.SetShaderParameter(VfxMaterials.Energy, energy);
        _draw.SetShaderParameter(VfxMaterials.Opacity, 1f);
        if (_ramp == VfxRamp.Heat)
        {
            // What the flame cools into: smoke, at the brightness of smoke whatever the flame's energy.
            Color cold = SmokeColour.Lerp(colors.Edge, 0.12f);
            float dim = 1f / Mathf.Max(0.1f, energy);
            _draw.SetShaderParameter(VfxMaterials.TintCool, new Color(cold.R * dim, cold.G * dim, cold.B * dim));
        }

        // Puffs are metres across and sit on the floor: without the depth fade each one is cut by the
        // ground in a hard line. Small added particles do without it, and so does the leanest tier.
        bool puff = _preset.Sprite == VfxSprite.Puff;
        _draw.SetShaderParameter(
            VfxMaterials.SoftDepth,
            puff && VfxQuality.Rich.SoftParticles ? Mathf.Clamp(0.4f * sizeScale, 0.2f, 1f) : 0f);

        // The engine culls an emitter by this box, not by where its particles are.
        float half = reach + (_preset.SizeMax * sizeScale) + 1f;
        _particles.VisibilityAabb = new Aabb(new Vector3(-half, -half, -half), new Vector3(half, half, half) * 2f);
        _particles.Lifetime = _life;
        _particles.AmountRatio = VfxBurstPresets.AmountRatio(spec.Density);
        _particles.OneShot = !spec.Continuous;
        _particles.Explosiveness = spec.Continuous ? 0f : _preset.Explosiveness;
        if (IsDeferred)
        {
            // Not placed yet: firing now would throw the particles from wherever the node was born.
            _particles.Emitting = false;
            return;
        }

        _particles.Restart();
        _particles.Emitting = true;
    }

    protected override void OnShown()
    {
        _particles.Restart();
        _particles.Emitting = true;
    }

    /// <summary>Changes how thick a continuous stream is, without restarting it.</summary>
    internal void SetDensity(float density) => _particles.AmountRatio = VfxBurstPresets.AmountRatio(density);

    protected override bool Tick(float delta)
    {
        if (_continuous)
        {
            if (!Stopping && _streamSeconds > 0f && Age >= _streamSeconds)
            {
                Stop();
            }

            // Stopped: emit no more, and stay until the last particle has died.
            return !Stopping || StopAge < _life;
        }

        return Age < VfxBurstPresets.BurstSeconds(_life, _preset.Explosiveness) + 0.1d;
    }

    protected override void OnStop()
    {
        if (_continuous)
        {
            _particles.Emitting = false;
        }
    }

    protected override void OnFinish() => _particles.Emitting = false;

    private static CurveTexture SizeOverLife(bool grow, VfxRamp ramp)
    {
        var curve = new Curve();
        if (grow)
        {
            curve.AddPoint(new Vector2(0f, 0.35f));
            curve.AddPoint(new Vector2(0.4f, 0.8f));
            curve.AddPoint(new Vector2(1f, 1f));
        }
        else if (ramp is VfxRamp.Solid or VfxRamp.Twinkle)
        {
            // A chunk of debris and a glint keep their size; they go by fading.
            curve.AddPoint(new Vector2(0f, 1f));
            curve.AddPoint(new Vector2(1f, 0.8f));
        }
        else
        {
            curve.AddPoint(new Vector2(0f, 1f));
            curve.AddPoint(new Vector2(0.6f, 0.7f));
            curve.AddPoint(new Vector2(1f, 0.1f));
        }

        return new CurveTexture { Curve = curve, Width = 32 };
    }

    private static GradientTexture1D ColourOverLife(VfxRamp ramp)
    {
        var gradient = new Gradient();
        switch (ramp)
        {
            case VfxRamp.Smoke:
                gradient.Offsets = new[] { 0f, 0.15f, 1f };
                gradient.Colors = new[]
                {
                    new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.7f), new Color(0.7f, 0.7f, 0.7f, 0f),
                };
                break;

            case VfxRamp.Heat:
                // Not a colour: red is brightness and green is heat (see vfx_sprite's `use_heat`).
                // White-hot for a moment, flame for the first half, smoke by three quarters.
                gradient.Offsets = new[] { 0f, 0.1f, 0.4f, 0.72f, 1f };
                gradient.Colors = new[]
                {
                    new Color(2f, 1f, 0f, 0f), new Color(1.6f, 1f, 0f, 1f), new Color(1f, 0.55f, 0f, 0.9f),
                    new Color(1f, 0f, 0f, 0.55f), new Color(1f, 0f, 0f, 0f),
                };
                break;

            case VfxRamp.Solid:
                gradient.Offsets = new[] { 0f, 0.7f, 1f };
                gradient.Colors = new[]
                {
                    new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f),
                };
                break;

            case VfxRamp.Soft:
                gradient.Offsets = new[] { 0f, 0.25f, 1f };
                gradient.Colors = new[]
                {
                    new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f),
                };
                break;

            case VfxRamp.Twinkle:
                gradient.Offsets = new[] { 0f, 0.12f, 0.3f, 0.45f, 0.62f, 0.8f, 1f };
                gradient.Colors = new[]
                {
                    new Color(1f, 1f, 1f, 0f), new Color(1.6f, 1.6f, 1.6f, 1f), new Color(1f, 1f, 1f, 0.15f),
                    new Color(1.4f, 1.4f, 1.4f, 1f), new Color(1f, 1f, 1f, 0.1f), new Color(1.2f, 1.2f, 1.2f, 0.8f),
                    new Color(1f, 1f, 1f, 0f),
                };
                break;

            default:
                // Past 1 at birth: a particle starts hotter than its colour and cools into it.
                gradient.Offsets = new[] { 0f, 0.3f, 1f };
                gradient.Colors = new[]
                {
                    new Color(1.7f, 1.7f, 1.7f, 1f), new Color(1f, 1f, 1f, 1f), new Color(0.55f, 0.55f, 0.55f, 0f),
                };
                break;
        }

        return new GradientTexture1D { Gradient = gradient, Width = 32, UseHdr = true };
    }
}
