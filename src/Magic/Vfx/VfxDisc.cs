using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxDisc"/> is asked to draw.</summary>
internal struct VfxDiscSpec
{
    public Vector3 Position;

    /// <summary>Metres from the centre to the rim.</summary>
    public float Radius;

    public VfxSchoolColors Colors;

    /// <summary>Seconds a one-shot lasts; a telegraph fills over exactly this long.</summary>
    public float Life;

    /// <summary>Holds until stopped (the floor of a zone).</summary>
    public bool Sustain;

    /// <summary>A bright front sweeps from the centre to the rim over <see cref="Life"/>.</summary>
    public bool Fills;

    /// <summary>Draws the rune circle on it.</summary>
    public bool Rune;

    /// <summary>Radians a second the rune turns. Held still under Reduced Motion.</summary>
    public float Spin;

    /// <summary>How fast the noise flows outward; negative flows inward.</summary>
    public float Flow;

    /// <summary>Strength of the soft noise disc inside the rim (0..1).</summary>
    public float Body;

    /// <summary>Strength of the rim line (0..1).</summary>
    public float Rim;

    /// <summary>Squashes the disc into a line: its depth against its width (1 = a circle).</summary>
    public float Depth;

    /// <summary>Stands upright facing the camera (a sigil in the air) instead of lying on the floor.</summary>
    public bool FaceCamera;

    /// <summary>Emission against the school's mid energy.</summary>
    public float Energy;

    public static VfxDiscSpec At(Vector3 position, float radius, in VfxSchoolColors colors) => new()
    {
        Position = position,
        Radius = radius,
        Colors = colors,
        Life = 1f,
        Flow = 0.3f,
        Body = 0.5f,
        Rim = 1f,
        Depth = 1f,
        Energy = 1f,
    };
}

/// <summary>
/// A disc of light on the floor (<c>vfx_ground.gdshader</c>): the flourish over a ground spell's
/// telegraph, the floor of a zone, the line a wall will stand on, and, turned to face the camera, a
/// sigil at a hand or over a target. Drawn on every tier (it is one quad), which matters on the two
/// lowest, where there are no ground-mark decals to show where a zone is.
/// </summary>
public partial class VfxDisc : VfxEffect
{
    /// <summary>Metres the quad sits above the point it was given, to clear the floor.</summary>
    public const float Lift = 0.09f;

    private readonly MeshInstance3D _quad;
    private readonly ShaderMaterial _material;

    private VfxDiscSpec _spec;
    private float _spin;

    public VfxDisc()
    {
        _material = VfxMaterials.Ground();
        _quad = new MeshInstance3D
        {
            Name = "Disc",
            Mesh = VfxMaterials.Plane,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Position = new Vector3(0f, Lift, 0f),
        };
        AddChild(_quad);
    }

    /// <summary>Styles the disc for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxDiscSpec spec)
    {
        _spec = spec;
        _spin = 0f;
        GlobalTransform = new Transform3D(Basis.Identity, spec.Position);

        float diameter = Mathf.Max(0.05f, spec.Radius) * 2f;
        float depth = spec.Depth <= 0f ? 1f : spec.Depth;
        _quad.Scale = new Vector3(diameter, 1f, diameter * depth);
        _quad.Position = spec.FaceCamera ? Vector3.Zero : new Vector3(0f, Lift, 0f);

        VfxSchoolColors colors = spec.Colors;
        _material.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
        _material.SetShaderParameter(VfxMaterials.Energy, colors.MidEnergy * (spec.Energy <= 0f ? 1f : spec.Energy));
        _material.SetShaderParameter(VfxMaterials.Pattern, spec.Rune ? VfxTextures.Rune : default(Variant));
        _material.SetShaderParameter(VfxMaterials.PatternMix, spec.Rune ? 1f : 0f);
        _material.SetShaderParameter(VfxMaterials.Flow, spec.Flow);
        _material.SetShaderParameter(VfxMaterials.Disc, spec.Body);
        _material.SetShaderParameter(VfxMaterials.Rim, spec.Rim);
        _material.SetShaderParameter(VfxMaterials.Spin, 0f);
        _material.SetShaderParameter(VfxMaterials.Fill, spec.Fills ? 0f : 1f);
        _material.SetShaderParameter(VfxMaterials.Opacity, 0f);
        Face();
    }

    protected override bool Tick(float delta)
    {
        Face();
        if (_spec.Spin != 0f && !VfxQuality.ReducedMotion)
        {
            _spin += _spec.Spin * delta;
            _material.SetShaderParameter(VfxMaterials.Spin, _spin);
        }

        float fadeIn = Mathf.Clamp((float)Age / 0.15f, 0f, 1f);
        if (Stopping)
        {
            float left = StopFade(StopFadeSeconds * 1.5f);
            _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn * left);
            return left > 0f;
        }

        if (_spec.Sustain)
        {
            _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn);
            return true;
        }

        float life = Mathf.Max(0.05f, _spec.Life);
        float t = (float)(Age / life);
        if (t >= 1f)
        {
            return false;
        }

        if (_spec.Fills)
        {
            // A telegraph: full strength the whole way, and the front arrives as the delay ends.
            _material.SetShaderParameter(VfxMaterials.Fill, t);
            _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn);
        }
        else
        {
            _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn * (1f - (t * t)));
        }

        return true;
    }

    /// <summary>Stands a camera-facing disc up toward the eye.</summary>
    private void Face()
    {
        if (!_spec.FaceCamera || Director is not { HasCamera: true } director)
        {
            return;
        }

        Vector3 toCamera = director.CameraPosition - GlobalPosition;
        if (toCamera.LengthSquared() < 0.0001f)
        {
            return;
        }

        // The quad's face is its local Y.
        Vector3 y = toCamera.Normalized();
        Vector3 reference = Mathf.Abs(y.Dot(Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        Vector3 x = reference.Cross(y).Normalized();
        GlobalBasis = new Basis(x, y, x.Cross(y));
    }
}
