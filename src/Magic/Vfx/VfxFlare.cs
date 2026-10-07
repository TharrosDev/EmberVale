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

    /// <summary>A short horizontal streak through the core, for the heaviest hits.</summary>
    public bool Streak;

    /// <summary>A burst of radial rays thrown out over the core: the structure of a blast's flash.
    /// Drawn where the tier has rays (<see cref="VfxRichness.Rays"/>).</summary>
    public bool Rays;

    /// <summary>Radial streaks in the wake of the shock ring (0..1).</summary>
    public float RingStreaks;

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

    /// <summary>Leave the core out and keep the halo (and the light): the soft glow behind a shaped
    /// head (<see cref="VfxMotif"/>) that is itself the bright part.</summary>
    public bool HaloOnly;

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
/// The flash at the heart of every effect: a hot core, an additive halo, an optional burst of rays,
/// an optional streak, an optional shock ring and an optional budgeted light.
///
/// <para>The core and halo are what make an effect read hot with glow switched off (the Performance
/// preset): the core is drawn at the school's core energy with its centre pushed to white, and the
/// halo lays a wide soft copy of the school colour behind it, which is the bloom the renderer is
/// not doing. Where glow is on, both are far past its threshold and bloom for real.</para>
///
/// <para><b>Bounded.</b> A flare scales with the blast it belongs to, and a blast is metres across,
/// so every soft layer here goes through <see cref="VfxCoverageRules"/> each frame: the white core
/// is capped in size and gone in a tenth of a second, the halo's quad is shrunk to what its tier may
/// span and its alpha is cut by how much of the frame it covers. What makes a large flash large is
/// the rays, the ring, and the blocks built around it, not a bigger disc.</para>
///
/// <para>One-shot by default (impacts, bursts, release snaps); sustained when asked (a wind-up aura
/// in the hand, the core of a bolt in flight, a totem's crown), in which case it flickers until
/// <see cref="VfxEffect.Stop"/> and then fades.</para>
/// </summary>
public partial class VfxFlare : VfxEffect
{
    /// <summary>The halo's size against the core's.</summary>
    public const float HaloScale = 2.5f;

    /// <summary>How much of a halo's quad is bright enough to count as coverage: the soft dot is
    /// below a tenth of its peak outside this fraction of its radius.</summary>
    public const float HaloCoverage = 0.55f;

    /// <summary>Where the shock ring sits on its mesh (see <c>vfx_ring.gdshader</c>).</summary>
    private const float RingAt = 0.8f;

    private const float PopSeconds = 0.06f;

    /// <summary>How white the middle of a one-shot flash is pushed, and of a glow that is held (an
    /// aura in the hand, the head of a bolt). A held glow at the flash's value is a white disc with
    /// a tinted rim, the same for every school; at this it is the school's colour all through.</summary>
    private const float FlashHotCore = 0.75f;

    private const float HeldHotCore = 0.18f;

    /// <summary>A held glow's core energy against the school's body energy: past the bloom
    /// threshold, short of clipping every channel (which is what turns any colour white).</summary>
    private const float HeldEnergy = 0.6f;

    /// <summary>A held glow's energy at the first-person casting point against its energy at fight
    /// distance, and its halo's alpha likewise. A metre and a half from the eye, with a halo laid
    /// over it, the full value clips every channel and the charge in the hand is a white disc
    /// whatever its school.</summary>
    private const float HeldNearEnergy = 0.55f;

    private const float HeldNearHalo = 0.5f;
    private const float FadeInSeconds = 0.1f;

    private readonly MeshInstance3D _core;
    private readonly MeshInstance3D _halo;
    private readonly MeshInstance3D _streak;
    private readonly MeshInstance3D _rays;
    private readonly MeshInstance3D _ring;
    private readonly OmniLight3D _light;
    private readonly ShaderMaterial _coreMaterial;
    private readonly ShaderMaterial _haloMaterial;
    private readonly ShaderMaterial _streakMaterial;
    private readonly ShaderMaterial _raysMaterial;
    private readonly ShaderMaterial _ringMaterial;

    private VfxFlareSpec _spec;
    private Basis _ringFacing = Basis.Identity;
    private bool _ringFlat = true;
    private bool _hasLight;
    private bool _hasShadow;
    private float _lightEnergy;
    private float _level = 1f;

    public VfxFlare()
    {
        _coreMaterial = VfxMaterials.Sprite(VfxTextures.Dot);
        _coreMaterial.SetShaderParameter(VfxMaterials.HotCore, FlashHotCore);
        _haloMaterial = VfxMaterials.Sprite(VfxTextures.Dot);
        _streakMaterial = VfxMaterials.Sprite(VfxTextures.Streak);
        _streakMaterial.SetShaderParameter(VfxMaterials.Spin, Mathf.Pi * 0.5f);
        _streakMaterial.SetShaderParameter(VfxMaterials.HotCore, 0.5f);
        _raysMaterial = VfxMaterials.Sprite(VfxTextures.Rays);
        _raysMaterial.SetShaderParameter(VfxMaterials.HotCore, 0.6f);
        _ringMaterial = VfxMaterials.Ring();

        _halo = Sprite("Halo", VfxMaterials.Quad, _haloMaterial);
        _rays = Sprite("Rays", VfxMaterials.Quad, _raysMaterial);
        _core = Sprite("Core", VfxMaterials.Quad, _coreMaterial);
        _streak = Sprite("Streak", VfxMaterials.Quad, _streakMaterial);
        _ring = Sprite("Ring", VfxMaterials.Annulus, _ringMaterial);

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
        VfxRichness rich = VfxQuality.Rich;
        bool core = !spec.NoCore;
        _core.Visible = core && !spec.HaloOnly;
        _halo.Visible = core;
        _streak.Visible = core && spec.Streak;
        _rays.Visible = core && spec.Rays && rich.Rays && !spec.Sustain;
        _ring.Visible = spec.Ring;

        float soft = rich.SoftParticles ? Mathf.Clamp(spec.Radius * 0.5f, 0.15f, 1.2f) : 0f;
        _coreMaterial.SetShaderParameter(VfxMaterials.HotCore, spec.Sustain ? HeldHotCore : FlashHotCore);
        _coreMaterial.SetShaderParameter(VfxMaterials.Tint, spec.Sustain ? colors.Mid : colors.Core);
        _coreMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
        _coreMaterial.SetShaderParameter(VfxMaterials.SoftDepth, soft);
        _haloMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
        _haloMaterial.SetShaderParameter(VfxMaterials.Energy, colors.MidEnergy);
        _haloMaterial.SetShaderParameter(VfxMaterials.SoftDepth, soft);
        _streakMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Core);
        _streakMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
        if (_rays.Visible)
        {
            _raysMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
            _raysMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);

            // No two blasts throw the same star.
            _raysMaterial.SetShaderParameter(VfxMaterials.Spin, (Serial * 2.399f) % Mathf.Tau);
        }

        if (spec.Ring)
        {
            _ringMaterial.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
            _ringMaterial.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
            _ringMaterial.SetShaderParameter(VfxMaterials.Radius, RingAt);
            _ringMaterial.SetShaderParameter(VfxMaterials.InnerGlow, 0f);
            _ringMaterial.SetShaderParameter(VfxMaterials.Breakup, spec.Sustain ? 0.3f : 0.75f);
            _ringMaterial.SetShaderParameter(VfxMaterials.Streaks, rich.Rays ? Mathf.Clamp(spec.RingStreaks, 0f, 1f) : 0f);
            _ringMaterial.SetShaderParameter(VfxMaterials.Spin, (Serial * 0.618f) % 1f);
            _ringFacing = Facing(spec.RingNormal);
            _ringFlat = _ringFacing == Basis.Identity;
        }

        _hasLight = false;
        _hasShadow = false;
        _light.Visible = false;
        if (spec.Light && Director is { } director && director.Ledger.TakeLight(VfxQuality.Budget.MaxLights))
        {
            _hasLight = true;
            _lightEnergy = Mathf.Clamp(colors.CoreEnergy * (0.35f + (spec.Radius * 0.5f)), 0.5f, 9f);
            _light.LightColor = colors.Mid;

            // A light's cost is every lit pixel inside its range, so the lean tiers keep it short.
            _light.OmniRange = Mathf.Min(Mathf.Max(1f, spec.LightRange), rich.LightRange);
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
        _coreMaterial.SetShaderParameter(VfxMaterials.Tint, _spec.Sustain ? colors.Mid : colors.Core);
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
        VfxTier tier = VfxQuality.Tier;
        float distance = Director is { HasCamera: true } director ? director.DistanceToCamera(GlobalPosition) : 8f;
        float coreSize;
        float haloSize;
        float coreAlpha;
        float haloAlpha;
        float raysAlpha = 0f;
        float raysSize = 0f;
        float ringRadius;
        float ringAlpha;
        float ringDepth;
        float light;
        float boost = 1f;
        float held = 1f;

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
            // Near the eye a glow is drawn hand-sized: the first-person casting point and the head
            // of a bolt just launched are a metre and a half out, where a 0.3 m glow is a disc over
            // a fifth of the frame. It grows to its full size over the first metres of its flight.
            float near = VfxCoverageRules.NearScale(distance);
            float share = VfxCoverageRules.NearShare(near);
            held = Mathf.Lerp(HeldNearEnergy, 1f, share);
            coreSize = radius * (0.45f + (0.55f * _level)) * flicker * near;
            haloSize = coreSize * HaloScale;
            coreAlpha = fade * (0.6f + (0.4f * _level));
            haloAlpha = fade * halo * (0.55f + (0.45f * _level)) * Mathf.Lerp(HeldNearHalo, 1f, share);
            ringRadius = Mathf.Max(0.05f, _spec.RingRadius);
            ringAlpha = fade * 0.75f;
            ringDepth = ringRadius * 0.12f;
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
            float swell = 0.5f + (0.75f * eased);

            // The white heart is capped and brief; the halo carries the size of the blast, faintly.
            coreSize = VfxCoverageRules.CoreRadius(radius) * swell;
            haloSize = radius * swell * HaloScale;
            coreAlpha = VfxCoverageRules.CoreAlpha(Age, Mathf.Max(0.05f, _spec.Life), radius);
            haloAlpha = halo * (1f - t) * Mathf.Sqrt(1f - t) * 1.2f;
            raysSize = radius * (1.1f + (1.5f * eased));
            raysAlpha = (1f - t) * (1f - t) * 0.85f;
            ringRadius = Mathf.Max(0.05f, _spec.RingRadius) * Mathf.Lerp(0.12f, 1f, easedAlong);
            ringAlpha = Mathf.Pow(1f - t, 1.3f);
            ringDepth = Mathf.Clamp(_spec.RingRadius * 0.14f, 0.08f, 0.7f);
            light = (1f - t) * (1f - t);

            // The first frames are hotter than the body of the flash: the pop.
            if (Age < PopSeconds)
            {
                boost = 1.7f;
            }

            // A flash at the eye (the release in first person) is held to half its size.
            float near = Mathf.Max(0.5f, VfxCoverageRules.NearScale(distance));
            coreSize *= near;
            haloSize *= near;
            raysSize *= near;
        }

        if (_core.Visible)
        {
            // The governor: a soft layer is shrunk to what its tier may span, then thinned by how
            // much of the frame it still covers.
            haloSize = VfxCoverageRules.ClampRadius(haloSize, distance, tier);
            haloAlpha *= VfxCoverageRules.Opacity(haloSize * HaloCoverage, distance, tier, _spec.Sustain ? 1d : Age);
            coreAlpha *= VfxCoverageRules.Opacity(coreSize * HaloCoverage, distance, tier, _spec.Sustain ? 1d : Age);

            _core.Scale = Vector3.One * (coreSize * 2f);
            _halo.Scale = Vector3.One * (haloSize * 2f);
            _coreMaterial.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(coreAlpha, 0f, 1f));
            _coreMaterial.SetShaderParameter(
                VfxMaterials.Energy,
                _spec.Sustain ? _spec.Colors.MidEnergy * HeldEnergy * held : _spec.Colors.CoreEnergy * boost);
            _haloMaterial.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(haloAlpha, 0f, 1f));
        }

        if (_rays.Visible)
        {
            // Rays are mostly empty quad, so they are governed on a fraction of their reach.
            raysSize = VfxCoverageRules.ClampRadius(raysSize, distance, tier);
            raysAlpha *= VfxCoverageRules.Opacity(raysSize * 0.4f, distance, tier, Age);
            _rays.Scale = Vector3.One * (raysSize * 2f);
            _raysMaterial.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(raysAlpha, 0f, 1f));
        }

        if (_streak.Visible)
        {
            // Long along the sprite's Y, then rolled a quarter turn by the material: a horizontal
            // streak, a few core widths long. (It was nine, which crossed the whole screen.)
            float length = VfxCoverageRules.ClampRadius(coreSize * 3.2f, distance, tier);
            _streak.Scale = new Vector3(coreSize * 0.6f, length, 1f);
            _streakMaterial.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(coreAlpha * 0.55f, 0f, 1f));
        }

        if (_ring.Visible)
        {
            // A ring on the floor about the camera itself (the player's own self-cast in first
            // person) is a band across the bottom of the view: it is cut right back while it is close.
            // Somebody else's ring that holds (a zone to get out of) is never cut.
            if (_ringFlat && (Own || !_spec.Sustain) && Director is { HasCamera: true } eye)
            {
                Vector3 toEye = eye.CameraPosition - GlobalPosition;
                ringAlpha *= VfxScreenRules.SelfRing(new Vector2(toEye.X, toEye.Z).Length(), toEye.Y, ringRadius);
            }

            // The ring is held at one place on its mesh and the mesh is scaled with it.
            float size = ringRadius * 2f / RingAt;
            _ring.Basis = new Basis(_ringFacing.X * size, _ringFacing.Y, _ringFacing.Z * size);
            _ringMaterial.SetShaderParameter(
                VfxMaterials.Thickness, Mathf.Clamp(ringDepth / Mathf.Max(0.01f, ringRadius) * RingAt, 0.05f, 0.34f));
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
        VfxMaterials.OnLayer(instance);
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
