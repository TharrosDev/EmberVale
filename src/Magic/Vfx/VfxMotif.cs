using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxMotif"/> is asked to be.</summary>
internal struct VfxMotifSpec
{
    public Vector3 Position;

    public VfxSchoolColors Colors;

    /// <summary>Overrides the colour taken from <see cref="Colors"/>. Alpha 0 = unset.</summary>
    public Color Tint { get; set; }

    /// <summary>What each piece is drawn with.</summary>
    public VfxSprite Sprite;

    /// <summary>How the pieces move.</summary>
    public VfxMotion Motion;

    /// <summary>How many pieces (1 to <see cref="VfxMotifRules.MaxCount"/>).</summary>
    public int Count;

    /// <summary>Metres the pattern reaches from its centre.</summary>
    public float Radius;

    /// <summary>A piece's length, metres.</summary>
    public float Size;

    /// <summary>A piece's width against its length.</summary>
    public float Aspect;

    /// <summary>The pace of the motion against its own (1 = as designed).</summary>
    public float Speed;

    /// <summary>Emission against the school's body energy.</summary>
    public float Energy;

    public float Opacity;

    /// <summary>Drawn in the school's pale core colour instead of its body colour.</summary>
    public bool Pale;

    /// <summary>Holds until stopped instead of lasting <see cref="Life"/> seconds.</summary>
    public bool Sustain;

    public float Life;

    /// <summary>Strength, 0..1 (a charge filling). Set later with <see cref="VfxMotif.SetLevel"/>.</summary>
    public float Level;

    /// <summary>For a <see cref="VfxMotion.Lance"/>: the line of flight.</summary>
    public Vector3 Axis;

    /// <summary>For a lance: how far the followers stray sideways, in lengths.</summary>
    public float Scatter;

    /// <summary>Drawn at its true size however near the eye it is. By default a motif shrinks near
    /// the camera exactly as a held glow does (<see cref="VfxCoverageRules.NearScale"/>), so the one
    /// in a first-person hand is the size of the hand.</summary>
    public bool TrueSize;

    public static VfxMotifSpec At(
        Vector3 position, in VfxSchoolColors colors, VfxSprite sprite, VfxMotion motion, int count, float radius,
        float size) => new()
    {
        Position = position,
        Colors = colors,
        Sprite = sprite,
        Motion = motion,
        Count = count,
        Radius = radius,
        Size = size,
        Aspect = 0.5f,
        Speed = 1f,
        Energy = 1f,
        Opacity = 1f,
        Life = 1f,
        Level = 1f,
    };

    /// <summary>A school's style (<see cref="VfxMotifRules"/>) around a glow of <paramref name="glow"/>
    /// metres, with as many pieces as the tier draws.</summary>
    public static VfxMotifSpec Of(Vector3 position, in VfxSchoolColors colors, in VfxMotifStyle style, float glow)
    {
        VfxMotifSpec spec = At(
            position, colors, style.Sprite, style.Motion, VfxMotifRules.CountAt(style, VfxQuality.Tier), glow * style.Reach,
            glow * style.Size);
        spec.Aspect = style.Aspect;
        spec.Energy = style.Energy;
        spec.Pale = style.Pale;
        spec.Scatter = style.Scatter;
        return spec;
    }
}

/// <summary>
/// A handful of shaped sprites moving in a pattern about a point: flames licking up off a hand,
/// shards of ice circling it, arcs crackling around it, leaves climbing a swirl, wisps drawn in, or
/// the shaped head of a bolt with its followers strung out behind. The structure that makes a school
/// read as itself where a glow and a stream of particles read as the same blob in six colours.
///
/// <para><b>One draw call.</b> The pieces are the instances of one <see cref="MultiMesh"/>, drawn
/// with the sprite shader; where each stands is <see cref="VfxMotifRules.Pose"/>, set from
/// <c>_Process</c>. No particle simulation, so on the leanest tiers a motif stands in for an emitter
/// rather than being added to one.</para>
///
/// <para>Under Reduced Motion the pattern runs at under a third of its pace, so nothing flickers.</para>
/// </summary>
public partial class VfxMotif : VfxEffect
{
    private const float FadeInSeconds = 0.12f;

    /// <summary>How far a motif's energy is cut at the eye: enough that a bright school keeps its
    /// colour in a first-person hand instead of clipping to white.</summary>
    private const float NearEnergy = 0.6f;

    /// <summary>The least of a lance's length that is ever seen. A bolt flying straight away from
    /// the eye (or at it) points along the line of sight, and a sprite laid along its flight would
    /// be seen end on: a sliver. Below this share it is laid along the flight as the eye sees it
    /// instead, and drawn at this share of its length.</summary>
    private const float LanceSeen = 0.6f;

    private readonly MultiMeshInstance3D _instance;
    private readonly MultiMesh _mesh;
    private readonly ShaderMaterial _material;

    private VfxMotifSpec _spec;
    private VfxSprite _sprite = VfxSprite.Mote;
    private float _level = 1f;
    private float _energy;
    private int _count;

    public VfxMotif()
    {
        _material = VfxMaterials.Sprite(VfxTextures.Mote);
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = VfxMaterials.Quad,
            InstanceCount = VfxMotifRules.MaxCount,
            VisibleInstanceCount = 0,
        };
        _instance = new MultiMeshInstance3D
        {
            Name = "Pieces",
            Multimesh = _mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 4f,
        };
        VfxMaterials.OnLayer(_instance);
        AddChild(_instance);
    }

    /// <summary>Styles the motif for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxMotifSpec spec)
    {
        _spec = spec;
        _level = Mathf.Clamp(spec.Level, 0f, 1f);
        _count = Mathf.Clamp(spec.Count, 1, VfxMotifRules.MaxCount);
        GlobalPosition = spec.Position;

        if (spec.Sprite != _sprite)
        {
            _sprite = spec.Sprite;
            _material.SetShaderParameter(VfxMaterials.Mask, VfxTextures.Sprite(spec.Sprite));
        }

        // Leaves on a swirl tumble facing the camera; everything else points the way it is going.
        _material.SetShaderParameter(VfxMaterials.AlignVelocity, spec.Motion == VfxMotion.Swirl ? 0f : 1f);
        Color tint = spec.Tint.A > 0f
            ? new Color(spec.Tint.R, spec.Tint.G, spec.Tint.B)
            : spec.Pale ? spec.Colors.Core : spec.Colors.Mid;
        _energy = spec.Colors.MidEnergy * (spec.Energy <= 0f ? 1f : spec.Energy);
        _material.SetShaderParameter(VfxMaterials.Tint, tint);
        _material.SetShaderParameter(VfxMaterials.Energy, _energy);
        _material.SetShaderParameter(VfxMaterials.Opacity, 1f);
        _mesh.VisibleInstanceCount = _count;
        Apply(0f);
    }

    /// <summary>A sustained motif's strength, 0..1: a charge filling in the hand.</summary>
    internal void SetLevel(float level) => _level = Mathf.Clamp(level, 0f, 1f);

    /// <summary>Turns a lance to a new line of flight.</summary>
    internal void Aim(Vector3 axis) => _spec.Axis = axis;

    protected override bool Tick(float delta)
    {
        // Stopped (or what it followed is gone): it fades where it is, sustained or not.
        if (Stopping && StopAge >= StopFadeSeconds)
        {
            return false;
        }

        float fade = Mathf.Clamp((float)Age / FadeInSeconds, 0f, 1f) * StopFade();
        if (!_spec.Sustain)
        {
            float life = Mathf.Max(0.05f, _spec.Life);
            float t = (float)(Age / life);
            if (t >= 1f)
            {
                return false;
            }

            fade *= Mathf.Clamp((1f - t) / 0.3f, 0f, 1f);
        }

        Apply(fade);
        return true;
    }

    protected override void OnFinish() => _mesh.VisibleInstanceCount = 0;

    private void Apply(float fade)
    {
        float near = 1f;
        if (!_spec.TrueSize && Director is { HasCamera: true } director)
        {
            near = VfxCoverageRules.NearScale(director.DistanceToCamera(GlobalPosition));
            float tame = Mathf.Lerp(NearEnergy, 1f, VfxCoverageRules.NearShare(near));
            _material.SetShaderParameter(VfxMaterials.Energy, _energy * tame);
        }

        float pace = (_spec.Speed <= 0f ? 1f : _spec.Speed) * (VfxQuality.ReducedMotion ? 0.3f : 1f);
        float time = (float)Age * pace;
        float radius = Mathf.Max(0f, _spec.Radius) * near;
        float size = Mathf.Max(0.01f, _spec.Size) * near;
        float aspect = _spec.Aspect <= 0f ? 0.5f : _spec.Aspect;
        float strength = Mathf.Clamp((_spec.Opacity <= 0f ? 1f : _spec.Opacity) * fade * (0.5f + (0.5f * _level)), 0f, 1f);
        float grown = 0.65f + (0.35f * _level);
        bool faces = _spec.Motion == VfxMotion.Swirl;
        bool lance = _spec.Motion == VfxMotion.Lance;
        Vector3 toEye = Vector3.Zero;
        Vector3 eyeUp = Vector3.Up;
        if (lance && Director is { HasCamera: true } eye)
        {
            toEye = eye.CameraPosition - GlobalPosition;
            toEye = toEye.LengthSquared() > 0.0001f ? toEye.Normalized() : Vector3.Zero;
            eyeUp = eye.CameraBasis.Y;
        }

        for (int i = 0; i < _count; i++)
        {
            VfxMotifPose pose = VfxMotifRules.Pose(
                _spec.Motion, i, _count, time, radius, size, _spec.Axis, _spec.Scatter, Serial);
            float length = size * pose.Scale * grown;
            float width = length * aspect;
            Basis basis;
            if (faces)
            {
                basis = new Basis(Vector3.Right * width, Vector3.Up * length, Vector3.Back);
            }
            else
            {
                Vector3 y = pose.Along;
                if (lance && toEye != Vector3.Zero)
                {
                    Vector3 across = y - (toEye * y.Dot(toEye));
                    float seen = across.Length();
                    if (seen < LanceSeen)
                    {
                        y = seen > 0.05f ? across / seen : eyeUp;
                        length *= LanceSeen;
                        width = length * aspect;
                    }
                }

                Vector3 x = VfxMotifRules.Perpendicular(y);
                basis = new Basis(x * width, y * length, x.Cross(y));
            }

            _mesh.SetInstanceTransform(i, new Transform3D(basis, pose.Offset));
            _mesh.SetInstanceColor(i, new Color(1f, 1f, 1f, Mathf.Clamp(pose.Alpha, 0f, 1f) * strength));
            _mesh.SetInstanceCustomData(i, new Color(pose.Roll, 0f, 0f, 0f));
        }
    }
}
