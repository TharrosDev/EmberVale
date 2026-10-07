using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxGroundMark"/> is asked to leave.</summary>
internal struct VfxGroundMarkSpec
{
    public VfxMark Mark;

    /// <summary>A point on (or a little above) the floor the mark is centred on.</summary>
    public Vector3 Position;

    /// <summary>Metres across.</summary>
    public float Size;

    public VfxSchoolColors Colors;

    /// <summary>Seconds the mark lasts before it has faded away.</summary>
    public float Life;

    /// <summary>Holds until stopped (a rune under a zone) instead of fading on its own.</summary>
    public bool Sustain;

    /// <summary>Radians a second it turns. Held still under Reduced Motion.</summary>
    public float Spin;

    /// <summary>Metres above and below <see cref="Position"/> the mark is projected through.</summary>
    public float Reach;

    /// <summary>Its depth against its width, for a mark that is a line (under a wall). 0 = square.</summary>
    public float Depth;
}

/// <summary>
/// What a spell leaves on the floor: a scorch, a patch of frost, a rune, a crack of roots. A
/// <see cref="Decal"/>, so it lies on whatever ground is there (slopes, steps, the terrain's own
/// shape) with no knowledge of it, and fades out.
///
/// <para>Marks have a budget of their own (<see cref="VfxBudget.GroundMarks"/>, none at all on the
/// two lowest tiers) and are not counted as live effects: a scorch that lasts eight seconds must not
/// hold the place of a spell being cast now. The director keeps them in order and fades the oldest
/// when the budget is full.</para>
/// </summary>
public partial class VfxGroundMark : VfxEffect
{
    private const float AppearSeconds = 0.12f;
    private const float FadeFraction = 0.45f;
    private const float StopSeconds = 0.5f;

    private readonly Decal _decal;

    private VfxGroundMarkSpec _spec;
    private Color _modulate;
    private float _emission;
    private float _heatSeconds;

    public VfxGroundMark()
    {
        _decal = new Decal
        {
            Name = "Decal",
            Size = new Vector3(1f, 3f, 1f),
            NormalFade = 0.35f,
            UpperFade = 0.4f,
            LowerFade = 0.4f,
            AlbedoMix = 1f,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 60f,
            DistanceFadeLength = 15f,

            // Not onto the effects themselves: a decal tints every surface in its box, and an
            // additive quad with a frost mark mixed into it is a pale square in the air.
            CullMask = VfxMaterials.DecalMask,
        };
        AddChild(_decal);
    }

    /// <summary>Styles the mark for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxGroundMarkSpec spec)
    {
        _spec = spec;
        GlobalTransform = new Transform3D(Basis.Identity, spec.Position);

        float size = Mathf.Max(0.05f, spec.Size);
        float reach = spec.Reach > 0f ? spec.Reach : Mathf.Clamp(size * 0.5f, 1f, 3f);
        _decal.Size = new Vector3(size, reach * 2f, size * (spec.Depth > 0f ? spec.Depth : 1f));
        _decal.TextureAlbedo = VfxTextures.MarkAlbedoOf(spec.Mark);
        _decal.TextureEmission = VfxTextures.MarkEmissionOf(spec.Mark);

        VfxSchoolColors colors = spec.Colors;
        switch (spec.Mark)
        {
            case VfxMark.Scorch:
                _modulate = Colors.White.Lerp(colors.Mid, 0.75f);
                _emission = 3.2f;
                _heatSeconds = 1.6f;
                break;

            case VfxMark.Frost:
                _modulate = Colors.White.Lerp(colors.Core, 0.45f);
                _emission = 0.7f;
                _heatSeconds = 2.5f;
                break;

            case VfxMark.Rune:
                _modulate = colors.Mid;
                _emission = 2.6f;
                _heatSeconds = 0f; // a rune glows for as long as it lasts
                break;

            default:
                _modulate = Colors.White.Lerp(colors.Mid, 0.3f);
                _emission = 0.6f;
                _heatSeconds = 1.2f;
                break;
        }

        Apply(0f, 1f);
    }

    protected override bool Tick(float delta)
    {
        if (_spec.Spin != 0f && !VfxQuality.ReducedMotion)
        {
            RotateY(_spec.Spin * delta);
        }

        float appear = Mathf.Clamp((float)Age / AppearSeconds, 0f, 1f);
        if (Stopping)
        {
            float left = StopFade(StopSeconds);
            Apply(appear * left, left);
            return left > 0f;
        }

        if (_spec.Sustain)
        {
            Apply(appear, 1f);
            return true;
        }

        float life = Mathf.Max(0.2f, _spec.Life);
        float t = (float)(Age / life);
        if (t >= 1f)
        {
            return false;
        }

        float fade = 1f - Mathf.Clamp((t - (1f - FadeFraction)) / FadeFraction, 0f, 1f);
        Apply(appear * fade, fade);
        return true;
    }

    private void Apply(float alpha, float glow)
    {
        _decal.Modulate = new Color(_modulate.R, _modulate.G, _modulate.B, Mathf.Clamp(alpha, 0f, 1f));
        float heat = _heatSeconds <= 0f ? 1f : Mathf.Clamp(1f - ((float)Age / _heatSeconds), 0f, 1f);
        _decal.EmissionEnergy = _emission * heat * heat * glow;
    }
}
