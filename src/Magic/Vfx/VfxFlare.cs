using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxFlare"/> is asked to be.</summary>
internal struct VfxFlareSpec
{
    public Vector3 Position;

    /// <summary>Metres from the centre to where the core glow has faded out.</summary>
    public float Radius;

    public VfxSchoolColors Colors;

    /// <summary>Seconds a one-shot lasts. Ignored by a sustained flare.</summary>
    public float Life;

    /// <summary>An expanding shock ring, out to <see cref="RingRadius"/> metres.</summary>
    public bool Ring;

    public float RingRadius;

    /// <summary>The way the ring faces. Zero = flat on the ground.</summary>
    public Vector3 RingNormal;

    /// <summary>The ring closes on the centre instead of leaving it.</summary>
    public bool Inward;

    /// <summary>Ask the light budget for an OmniLight of <see cref="LightRange"/> metres.</summary>
    public bool Light;

    public float LightRange;

    /// <summary>A wide horizontal streak through the core, for the heaviest hits.</summary>
    public bool Streak;

    /// <summary>Holds until stopped (an aura, a bolt's core) instead of flashing once.</summary>
    public bool Sustain;

    /// <summary>A sustained flare's strength, 0..1 (a charge filling). Set later with
    /// <see cref="VfxFlare.SetLevel"/>.</summary>
    public float Level;

    /// <summary>A sustained flare climbs from <see cref="Level"/> to full over this many seconds
    /// (a wind-up building to its release). 0 = it holds its level.</summary>
    public float RampSeconds;

    /// <summary>Leave the core and halo out: the ring (and the light) alone.</summary>
    public bool NoCore;

    public static VfxFlareSpec At(Vector3 position, float radius, in VfxSchoolColors colors) => new()
    {
        Position = position,
        Radius = radius,
        Colors = colors,
        Life = 0.34f,
        Level = 1f,
    };
}

/// <summary>
/// The flash at the heart of every effect: a hot core, an additive halo two and a half times its
/// size, an optional streak, an optional shock ring and an optional budgeted light.
///
/// <para>The core and halo are what make an effect read hot with glow switched off (the Performance
/// preset): the core is drawn at the school's core energy with its centre pushed to white, and the
/// halo lays a wide soft copy of the school colour behind it, which is the bloom the renderer is
/// not doing. Where glow is on, both are far past its threshold and bloom for real.</para>
///
/// <para>One-shot by default (impacts, bursts, release snaps); sustained when asked (a wind-up aura
/// in the hand, the core of a bolt in flight, a totem's crown), in which case it flickers until
/// <see cref="VfxEffect.Stop"/> and then fades.</para>
/// </summary>
public partial class VfxFlare : VfxEffect
{
    /// <summary>The halo's size against the core's.</summary>
    public const float HaloScale = 2.5f;

    private const float PopSeconds = 0.06f;
    private const float FadeInSeconds = 0.1f;

    private readonly MeshInstance3D _core;
    private readonly MeshInstance3D _halo;
    private readonly MeshInstance3D _streak;
    private readonly MeshInstance3D _ring;
    private readonly OmniLight3D _light;
    private readonly ShaderMaterial _coreMaterial;
    private readonly ShaderMaterial _haloMaterial;
    private readonly ShaderMaterial _streakMaterial;
    private readonly ShaderMaterial _ringMaterial;

    private VfxFlareSpec _spec;
    private bool _hasLight;
    private bool _hasShadow;
    private float _lightEnergy;
    private float _level = 1f;

    public VfxFlare()
    {
        _coreMaterial = VfxMaterials.Sprite(VfxTextures.Dot);
        _coreMaterial.SetShaderParameter(VfxMaterials.HotCore, 0.75f);
        _haloMaterial = VfxMaterials.Sprite(VfxTextures.Dot);
        _streakMaterial = VfxMaterials.Sprite(VfxTextures.Streak);
        _streakMaterial.SetShaderParameter(VfxMaterials.Spin, Mathf.Pi * 0.5f);
        _streakMaterial.SetShaderParameter(VfxMaterials.HotCore, 0.5f);
        _ringMaterial = VfxMaterials.Ring();

        _halo = Sprite("Halo", VfxMaterials.Quad, _haloMaterial);
        _core = Sprite("Core", VfxMaterials.Quad, _coreMaterial);
        _streak = Sprite("Streak", VfxMaterials.Quad, _streakMaterial);
        _ring = Sprite("Ring", VfxMaterials.Plane, _ringMaterial);

        _light = new OmniLight3D
        {
            Name = "Light",
            Visible = false,
            ShadowEnabled = false,
            OmniAttenuation = 1.4f,
            LightSpecular = 0.3f,
        };
        AddChild(_light);
    }

    /// <summary>Styles the flare for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxFlareSpec spec)
    {
        _spec = spec;
        _level = Mathf.Clamp(spec.Level, 0f, 1f);
        GlobalPosition = spec.Position;

        VfxSchoolColors colors = spec.Colors;
        bool core = !spec.NoCore;
        _core.Visible = core;
        _halo.Visible = core;
        _streak.Visible = core && spec.Streak;
        _ring.Visible = spec.Ring;

        float soft = VfxQuality.Tier == VfxTier.Performance ? 0f : Mathf.Clamp(spec.Radius * 0.5f, 0.15f, 1.2f);
        _coreMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Core);
        _coreMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
        _coreMaterial.SetShaderParameter(VfxMaterials.SoftDepth, soft);
        _haloMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
        _haloMaterial.SetShaderParameter(VfxMaterials.Energy, colors.MidEnergy);
        _haloMaterial.SetShaderParameter(VfxMaterials.SoftDepth, soft);
        _streakMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Core);
        _streakMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);

        if (spec.Ring)
        {
            _ringMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
            _ringMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
            _ringMaterial.SetShaderParameter(VfxMaterials.InnerGlow, spec.Sustain ? 0.12f : 0.3f);
            _ringMaterial.SetShaderParameter(VfxMaterials.Breakup, spec.Sustain ? 0.2f : 0.4f);
            _ringMaterial.SetShaderParameter(VfxMaterials.Spin, 0f);

            // The shader draws the ring out to 0.9 of the quad's half size.
            float size = Mathf.Max(0.05f, spec.RingRadius) * 2f / 0.9f;
            Basis facing = Facing(spec.RingNormal);
            _ring.Basis = new Basis(facing.X * size, facing.Y, facing.Z * size);
        }

        _hasLight = false;
        _hasShadow = false;
        _light.Visible = false;
        if (spec.Light && Director is { } director && director.Ledger.TakeLight(VfxQuality.Budget.MaxLights))
        {
            _hasLight = true;
            _lightEnergy = Mathf.Clamp(colors.CoreEnergy * (0.35f + (spec.Radius * 0.5f)), 0.5f, 9f);
            _light.LightColor = colors.Mid;
            _light.OmniRange = Mathf.Max(1f, spec.LightRange);
            _light.LightEnergy = spec.Sustain ? 0f : _lightEnergy;
            _hasShadow = !spec.Sustain && spec.Radius >= 1.5f &&
                         director.Ledger.TakeShadow(VfxQuality.Budget.ShadowedLights);
            _light.ShadowEnabled = _hasShadow;
            _light.Visible = true;
        }

        Apply(0f);
    }

    /// <summary>A sustained flare's strength, 0..1: a charge filling in the hand.</summary>
    internal void SetLevel(float level) => _level = Mathf.Clamp(level, 0f, 1f);

    /// <summary>Changes a sustained flare's colours mid-life (a wind-up handing over to a channel).</summary>
    internal void Recolor(in VfxSchoolColors colors)
    {
        _spec.Colors = colors;
        _coreMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Core);
        _haloMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
    }

    protected override bool Tick(float delta)
    {
        if (_spec.Sustain)
        {
            if (Stopping && StopAge >= StopFadeSeconds)
            {
                return false;
            }

            Apply(0f);
            return true;
        }

        float life = Mathf.Max(0.05f, _spec.Life);
        float t = (float)(Age / life);
        if (t >= 1f)
        {
            return false;
        }

        Apply(t);
        return true;
    }

    protected override void OnFinish()
    {
        _light.Visible = false;
        if (_hasLight)
        {
            Director?.Ledger.ReturnLight();
            _hasLight = false;
        }

        if (_hasShadow)
        {
            Director?.Ledger.ReturnShadow();
            _hasShadow = false;
            _light.ShadowEnabled = false;
        }
    }

    private void Apply(float t)
    {
        float radius = Mathf.Max(0.01f, _spec.Radius);
        float halo = _spec.Colors.Halo;
        float coreSize;
        float coreAlpha;
        float haloAlpha;
        float ringAt;
        float ringAlpha;
        float ringThickness;
        float light;
        float boost = 1f;

        if (_spec.Sustain)
        {
            float fade = Mathf.Clamp((float)Age / FadeInSeconds, 0f, 1f) * StopFade();
            if (_spec.RampSeconds > 0f)
            {
                _level = Mathf.Lerp(
                    Mathf.Clamp(_spec.Level, 0f, 1f), 1f, Mathf.Clamp((float)Age / _spec.RampSeconds, 0f, 1f));
            }

            float flicker = VfxQuality.ReducedMotion
                ? 1f
                : 1f + (0.07f * Mathf.Sin((float)Age * 17f)) + (0.04f * Mathf.Sin(((float)Age * 31f) + 1.3f));
            coreSize = radius * (0.45f + (0.55f * _level)) * flicker;
            coreAlpha = fade * (0.6f + (0.4f * _level));
            haloAlpha = fade * halo * (0.55f + (0.45f * _level));
            ringAt = 0.86f;
            ringAlpha = fade * 0.75f;
            ringThickness = 0.1f;
            light = fade * flicker * (0.35f + (0.65f * _level)) * 0.45f;
            if (_spec.Ring && !VfxQuality.ReducedMotion)
            {
                _ringMaterial.SetShaderParameter(VfxMaterials.Spin, (float)Age * 0.18f);
            }
        }
        else
        {
            float along = _spec.Inward ? 1f - t : t;
            float eased = 1f - ((1f - t) * (1f - t) * (1f - t));
            float easedAlong = 1f - ((1f - along) * (1f - along) * (1f - along));
            coreSize = radius * (0.5f + (0.75f * eased));
            coreAlpha = 1f - Mathf.SmoothStep(0.12f, 1f, t);
            // A little over its resting alpha at the pop, and thinner the wider it is: a blast's halo
            // is metres across, and at full strength it would white out the frame it is drawn in.
            haloAlpha = halo * (1f - t) * Mathf.Sqrt(1f - t) * 1.2f / (1f + (0.12f * radius));
            ringAt = Mathf.Lerp(0.1f, 0.9f, easedAlong);
            ringAlpha = Mathf.Pow(1f - t, 1.3f);
            ringThickness = Mathf.Lerp(0.24f, 0.07f, along);
            light = (1f - t) * (1f - t);

            // The first frames are hotter than the body of the flash: the pop.
            if (Age < PopSeconds)
            {
                boost = 1.7f;
            }
        }

        if (_core.Visible)
        {
            _core.Scale = Vector3.One * (coreSize * 2f);
            _halo.Scale = Vector3.One * (coreSize * 2f * HaloScale);
            _coreMaterial.SetShaderParameter(VfxMaterials.Opacity, coreAlpha);
            _coreMaterial.SetShaderParameter(VfxMaterials.Energy, _spec.Colors.CoreEnergy * boost);
            _haloMaterial.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(haloAlpha, 0f, 1f));
        }

        if (_streak.Visible)
        {
            // Long along the sprite's Y, then rolled a quarter turn by the material: a horizontal streak.
            _streak.Scale = new Vector3(coreSize * 0.9f, coreSize * 9f, 1f);
            _streakMaterial.SetShaderParameter(VfxMaterials.Opacity, coreAlpha * 0.8f);
        }

        if (_ring.Visible)
        {
            _ringMaterial.SetShaderParameter(VfxMaterials.Radius, ringAt);
            _ringMaterial.SetShaderParameter(VfxMaterials.Thickness, ringThickness);
            _ringMaterial.SetShaderParameter(VfxMaterials.Opacity, ringAlpha);
        }

        if (_hasLight)
        {
            _light.LightEnergy = _lightEnergy * light;
        }
    }

    private MeshInstance3D Sprite(string name, Mesh mesh, Material material)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 4f,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>A basis whose Y axis is <paramref name="normal"/> (up when there is none).</summary>
    private static Basis Facing(Vector3 normal)
    {
        if (normal.LengthSquared() < 0.0001f || Mathf.Abs(normal.Normalized().Dot(Vector3.Up)) > 0.98f)
        {
            return Basis.Identity;
        }

        Vector3 y = normal.Normalized();
        Vector3 x = Vector3.Up.Cross(y).Normalized();
        Vector3 z = x.Cross(y);
        return new Basis(x, y, z);
    }
}
