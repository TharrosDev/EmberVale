using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What a <see cref="VfxDisc"/> is patterned with.</summary>
public enum VfxDiscPattern
{
    None,

    /// <summary>The rune circle: thin lines, drawn at full strength.</summary>
    Rune,

    /// <summary>Hoar frost: six crystalline spokes and their feathering.</summary>
    Frost,

    /// <summary>Glowing cracks, as in a fresh scorch: the ground about to break open.</summary>
    Cracks,

    /// <summary>Roots cracking outward from the centre.</summary>
    Roots,
}

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

    /// <summary>Draws the rune circle on it. The same as <see cref="Pattern"/> = Rune.</summary>
    public bool Rune;

    /// <summary>What the disc is patterned with (frost, cracks, roots), when it is not a rune.</summary>
    public VfxDiscPattern Pattern;

    /// <summary>The pattern spreads out from the centre over this many seconds (frost creeping
    /// across the floor): on a sustained disc, or on a one-shot that is not a telegraph
    /// (<see cref="Fills"/>), which then fades over the rest of its <see cref="Life"/>.
    /// 0 = it is there at once.</summary>
    public float SpreadSeconds;

    /// <summary>Radians a second the rune turns. Held still under Reduced Motion.</summary>
    public float Spin;

    /// <summary>How fast the noise flows outward; negative flows inward.</summary>
    public float Flow;

    /// <summary>Strength of the soft noise wisps inside the rim (0..1).</summary>
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
/// sigil at a hand or over a target. Drawn on every tier, which matters on the two lowest, where
/// there are no ground-mark decals to show where a zone is.
///
/// <para>It is a rim, a pattern and wisps, not a filled disc. The soft body is governed
/// (<see cref="VfxCoverageRules"/>): a zone the camera stands in would otherwise tint the whole
/// floor of the frame. On the leanest tier a standing zone's floor is drawn on an annulus, rim only,
/// so its middle costs nothing; a telegraph is always whole, because its filling front is the
/// warning.</para>
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
        VfxMaterials.OnLayer(_quad);
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
        VfxDiscPattern pattern = spec.Rune ? VfxDiscPattern.Rune : spec.Pattern;
        bool lined = pattern == VfxDiscPattern.Rune;

        // Rim only where the tier draws no full floor: a standing, unpatterned disc on the ground.
        bool rimOnly = !VfxQuality.Rich.FullDisc && spec.Sustain && !spec.Fills && !spec.FaceCamera && !lined;
        _quad.Mesh = rimOnly ? VfxMaterials.RimAnnulus : VfxMaterials.Plane;
        _quad.Scale = new Vector3(diameter, 1f, diameter * depth);
        _quad.Position = spec.FaceCamera ? Vector3.Zero : new Vector3(0f, Lift, 0f);

        VfxSchoolColors colors = spec.Colors;
        Texture2D? texture = rimOnly ? null : VfxTextures.Pattern(pattern);
        _material.SetShaderParameter(VfxMaterials.Tint, colors.Mid);
        _material.SetShaderParameter(VfxMaterials.Energy, colors.MidEnergy * (spec.Energy <= 0f ? 1f : spec.Energy));
        _material.SetShaderParameter(VfxMaterials.Pattern, texture != null ? texture : default(Variant));
        _material.SetShaderParameter(VfxMaterials.PatternMix, texture != null ? (lined ? 1f : 0.8f) : 0f);

        // A rune is thin lines and is drawn as it is. The others have a soft base that would fill
        // the disc: only what is above it shows, and the governor may thin it.
        _material.SetShaderParameter(VfxMaterials.PatternFloor, lined ? 0f : pattern == VfxDiscPattern.Cracks ? 0.25f : 0.5f);
        _material.SetShaderParameter(VfxMaterials.PatternSoft, lined ? 0f : 0.6f);
        _material.SetShaderParameter(VfxMaterials.Reveal, spec.SpreadSeconds > 0f && !spec.Fills ? 1f : 0f);
        _material.SetShaderParameter(VfxMaterials.Flow, spec.Flow);
        _material.SetShaderParameter(VfxMaterials.Disc, rimOnly ? 0f : spec.Body);
        _material.SetShaderParameter(VfxMaterials.Rim, spec.Rim);
        _material.SetShaderParameter(VfxMaterials.Spin, 0f);
        _material.SetShaderParameter(VfxMaterials.Fill, spec.Fills || spec.SpreadSeconds > 0f ? 0f : 1f);
        _material.SetShaderParameter(VfxMaterials.BodyOpacity, 1f);
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

        if (!_spec.FaceCamera && Director is { HasCamera: true } director)
        {
            // The governor, on the soft body alone: the rim, the front and a rune's lines stay.
            _material.SetShaderParameter(
                VfxMaterials.BodyOpacity,
                VfxCoverageRules.Opacity(_spec.Radius * 0.8f, director.DistanceToCamera(GlobalPosition), VfxQuality.Tier, 1d));
        }

        float fadeIn = Mathf.Clamp((float)Age / 0.15f, 0f, 1f);
        if (Stopping)
        {
            float left = StopFade(StopFadeSeconds * 1.5f);
            _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn * left);
            return left > 0f;
        }

        if (_spec.SpreadSeconds > 0f && !_spec.Fills)
        {
            float spread = Mathf.Clamp((float)Age / _spec.SpreadSeconds, 0f, 1f);
            _material.SetShaderParameter(VfxMaterials.Fill, 1f - ((1f - spread) * (1f - spread)));
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
