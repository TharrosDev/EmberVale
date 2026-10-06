using System.Collections.Generic;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What a <see cref="VfxBolt"/> ribbon is being used as.</summary>
internal enum VfxBoltMode
{
    /// <summary>A lightning strike: jagged, branched, re-jittered twice and gone in a fifth of a second.</summary>
    Lightning,

    /// <summary>A sustained beam, re-jittered at <see cref="VfxBolt.BeamJitterHz"/> until it stops
    /// being refreshed.</summary>
    Beam,

    /// <summary>A smooth flowing line between two things: a life tether, a heal, a status jumping.</summary>
    Tether,

    /// <summary>The trail behind something that moves.</summary>
    Trail,
}

/// <summary>What one <see cref="VfxBolt"/> is asked to draw.</summary>
internal struct VfxBoltSpec
{
    public VfxBoltMode Mode;
    public Vector3 From;
    public Vector3 To;
    public VfxSchoolColors Colors;

    /// <summary>Half width of the bright line, metres.</summary>
    public float Width;

    /// <summary>Sideways kink as a fraction of the length. 0 = the mode's own.</summary>
    public float Jitter;

    public int Segments;
    public int Branches;

    /// <summary>Seconds a one-shot lasts. 0 = the mode's own.</summary>
    public float Life;

    /// <summary>Seconds of path a trail keeps.</summary>
    public float TrailSeconds;

    /// <summary>A beam or tether drawn as electricity: a hair-thin white line in a coloured fringe,
    /// in several flickering strands. A <see cref="VfxBoltMode.Lightning"/> strike always is.</summary>
    public bool Electric;

    public int Seed;
}

/// <summary>
/// The ribbon block: lightning, beams, tethers and projectile trails, all one mesh and one shader.
///
/// <para>The mesh is only a centre line (see <c>vfx_ribbon.gdshader</c>, which widens it toward the
/// camera), built in world space on a node that stays at the origin. The line itself comes from
/// <see cref="VfxBoltPath"/>, which is seeded: a beam whose ends move is rebuilt every frame from
/// the same seed and so holds its shape, and only changes when it is re-jittered. Under Reduced
/// Motion nothing is re-jittered at all: a bolt is one still shape that fades.</para>
///
/// <para>Each bolt is drawn twice in the one mesh: the line, and a wide faint copy under it. That
/// copy is the bolt's halo, the glow the renderer does not add when bloom is off.</para>
///
/// <para><b>Electricity</b> (a lightning strike, or a beam or tether marked
/// <see cref="VfxBoltSpec.Electric"/>) is not a glowing tube: the line is a third of the width asked
/// for and white-hot, the fringe is the school colour, the path has twice the tier's segments and a
/// bounded sway so it is jagged rather than bent, one or two thinner strands weave round the main
/// one (<see cref="VfxRichness.BoltStrands"/>), and it flickers. Forks are
/// <see cref="VfxBoltSpec.Branches"/>.</para>
/// </summary>
public partial class VfxBolt : VfxEffect
{
    /// <summary>How often a sustained beam changes shape.</summary>
    public const float BeamJitterHz = 15f;

    /// <summary>How long a lightning strike lasts, and how many times it changes shape in that time.</summary>
    public const float StrikeSeconds = 0.18f;

    public const int StrikeRejitters = 2;

    private const float HaloWidth = 3.6f;
    private const float HaloAlpha = 0.28f;

    /// <summary>An electric line's width against the width asked for, and its fringe against the line.</summary>
    private const float ElectricWidth = 0.34f;

    private const float ElectricFringe = 6f;
    private const float ElectricFringeAlpha = 0.14f;

    /// <summary>The furthest an electric path may sway from its straight line, metres: past this a
    /// long bolt is a bent tube, not a jagged one.</summary>
    private const float StrikeSway = 0.55f;

    private const float BeamSway = 0.22f;
    private const int MaxSegments = 48;
    private const int MaxTrailPoints = 40;

    private readonly ImmediateMesh _mesh = new();
    private readonly MeshInstance3D _instance;
    private readonly ShaderMaterial _material;
    private readonly List<Vector3> _points = new();
    private readonly List<Vector3> _branchPoints = new();
    private readonly List<int> _branchStarts = new();
    private readonly List<Vector3> _scratch = new();
    private readonly List<(Vector3 Position, double At)> _history = new();

    private VfxBoltSpec _spec;
    private VfxAnchor _fromAnchor;
    private VfxAnchor _toAnchor;
    private bool _followsFrom;
    private bool _followsTo;
    private Vector3 _settleOffset;
    private float _settleSeconds = VfxRecipeRules.HandSettleSeconds;
    private bool _settleAccelerates;
    private int _seed;
    private bool _electric;
    private int _jitters;
    private double _expireAt;
    private float _life;

    public VfxBolt()
    {
        _material = VfxMaterials.Ribbon();
        _instance = new MeshInstance3D
        {
            Name = "Ribbon",
            Mesh = _mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 2f,
        };
        VfxMaterials.OnLayer(_instance);
        AddChild(_instance);
    }

    /// <summary>Styles the ribbon for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxBoltSpec spec)
    {
        _spec = spec;
        _seed = spec.Seed;
        _electric = spec.Mode == VfxBoltMode.Lightning ||
                    (spec.Electric && spec.Mode is VfxBoltMode.Beam or VfxBoltMode.Tether);
        _jitters = 0;
        _followsFrom = false;
        _followsTo = false;
        _settleOffset = Vector3.Zero;
        _history.Clear();
        _expireAt = double.MaxValue;
        _life = spec.Life > 0f
            ? spec.Life
            : spec.Mode == VfxBoltMode.Tether ? 0.38f : StrikeSeconds;
        if (_spec.Jitter <= 0f)
        {
            _spec.Jitter = spec.Mode switch
            {
                VfxBoltMode.Lightning => 0.11f,
                VfxBoltMode.Beam => 0.045f,
                VfxBoltMode.Tether => 0.05f,
                _ => 0f,
            };
        }

        // The mesh is in world space; the node never leaves the origin.
        GlobalTransform = Transform3D.Identity;

        VfxSchoolColors colors = spec.Colors;
        _material.SetShaderParameter(VfxMaterials.ColorCore, colors.Core);
        _material.SetShaderParameter(VfxMaterials.ColorEdge, colors.Mid);
        _material.SetShaderParameter(VfxMaterials.Energy, colors.CoreEnergy);
        _material.SetShaderParameter(VfxMaterials.Opacity, 1f);
        _material.SetShaderParameter(VfxMaterials.WidthScale, 1f);
        bool flows = !_electric && spec.Mode is VfxBoltMode.Tether or VfxBoltMode.Beam;
        _material.SetShaderParameter(VfxMaterials.WhiteCore, _electric ? 0.85f : 0f);
        _material.SetShaderParameter(VfxMaterials.CoreWidth, _electric ? 0.5f : 0.28f);
        _material.SetShaderParameter(VfxMaterials.FlowAmount, flows ? 0.75f : spec.Mode == VfxBoltMode.Trail ? 0.45f : 0f);
        _material.SetShaderParameter(VfxMaterials.FlowSpeed, spec.Mode == VfxBoltMode.Tether ? 2.5f : 4f);
        _material.SetShaderParameter(VfxMaterials.UvScale, Mathf.Max(1f, spec.From.DistanceTo(spec.To) * 0.4f));

        if (spec.Mode == VfxBoltMode.Trail)
        {
            // A deferred trail does not know where it starts until its first frame.
            if (!IsDeferred)
            {
                _history.Add((spec.From, 0d));
            }

            _mesh.ClearSurfaces();
        }
        else
        {
            Rebuild();
        }
    }

    /// <summary>The start of a beam or tether follows <paramref name="anchor"/>.</summary>
    internal void FollowFrom(in VfxAnchor anchor)
    {
        _fromAnchor = anchor;
        _followsFrom = true;
    }

    /// <summary>The end of a beam or tether (or the head of a trail) follows <paramref name="anchor"/>.</summary>
    internal void FollowTo(in VfxAnchor anchor)
    {
        _toAnchor = anchor;
        _followsTo = true;
    }

    /// <summary>A trail's head starts <paramref name="offset"/> off what it follows and settles onto
    /// it, exactly as <see cref="VfxEffect.SettleFrom"/> moves the blocks it trails behind.</summary>
    internal void SettleTrailFrom(
        Vector3 offset, float seconds = VfxRecipeRules.HandSettleSeconds, bool accelerate = false)
    {
        _settleOffset = offset;
        _settleSeconds = seconds;
        _settleAccelerates = accelerate;
    }

    /// <summary>Moves a beam's ends and keeps it alive for <paramref name="holdSeconds"/> more.</summary>
    internal void Refresh(Vector3 from, Vector3 to, float holdSeconds)
    {
        _spec.From = from;
        _spec.To = to;
        _expireAt = Age + holdSeconds;
    }

    /// <summary>Where the beam or tether ends now.</summary>
    internal Vector3 End => _spec.To;

    protected override bool Tick(float delta)
    {
        switch (_spec.Mode)
        {
            case VfxBoltMode.Trail:
                return TickTrail();

            case VfxBoltMode.Beam:
                return TickBeam();

            default:
                return TickStrike();
        }
    }

    private bool TickStrike()
    {
        float t = (float)(Age / _life);
        if (t >= 1f)
        {
            return false;
        }

        bool moved = ResolveEnds();
        bool lightning = _spec.Mode == VfxBoltMode.Lightning;
        int due = lightning && !VfxQuality.ReducedMotion ? Mathf.Min(StrikeRejitters, (int)(t * (StrikeRejitters + 1))) : 0;
        if (due != _jitters)
        {
            _jitters = due;
            _seed = unchecked(_spec.Seed + (due * 7919));
            moved = true;
        }

        if (moved)
        {
            Rebuild();
        }

        // A strike flickers back up on each new shape; a tether swells and dies smoothly.
        float fade = lightning
            ? (1f - (t * t)) * (0.7f + (0.3f * (1f - ((t * (StrikeRejitters + 1)) % 1f))))
            : Mathf.Sin(Mathf.Min(1f, t * 1.15f) * Mathf.Pi) * Flicker();
        _material.SetShaderParameter(VfxMaterials.Opacity, Mathf.Clamp(fade, 0f, 1f));
        return true;
    }

    private bool TickBeam()
    {
        if (!Stopping && Age > _expireAt)
        {
            Stop();
        }

        if (Stopping && StopAge >= StopFadeSeconds)
        {
            return false;
        }

        ResolveEnds();
        if (!VfxQuality.ReducedMotion)
        {
            _seed = unchecked(_spec.Seed + ((int)(Age * BeamJitterHz) * 7919));
        }

        Rebuild();
        float fadeIn = Mathf.Clamp((float)Age / 0.06f, 0f, 1f);
        _material.SetShaderParameter(VfxMaterials.Opacity, fadeIn * StopFade() * Flicker());
        return true;
    }

    /// <summary>Electricity is never steady: a fast, uneven dip in brightness. 1 for anything else,
    /// and under Reduced Motion.</summary>
    private float Flicker()
    {
        if (!_electric || VfxQuality.ReducedMotion)
        {
            return 1f;
        }

        float age = (float)Age;
        return 0.78f + (0.14f * Mathf.Sin(age * 83f)) + (0.08f * Mathf.Sin((age * 141f) + 1.7f));
    }

    private bool TickTrail()
    {
        float keep = Mathf.Max(0.02f, _spec.TrailSeconds);
        if (!Stopping)
        {
            if (_followsTo && _toAnchor.TryResolve(out Vector3 head))
            {
                head += _settleOffset * VfxRecipeRules.SettleLeft(Age, _settleSeconds, _settleAccelerates);
                if (_history.Count == 0 || _history[_history.Count - 1].Position.DistanceSquaredTo(head) > 0.0016f)
                {
                    _history.Add((head, Age));
                    if (_history.Count > MaxTrailPoints)
                    {
                        _history.RemoveAt(0);
                    }
                }
            }
            else if (_followsTo)
            {
                Stop();
            }
        }

        while (_history.Count > 0 && Age - _history[0].At > keep)
        {
            _history.RemoveAt(0);
        }

        if (_history.Count < 2)
        {
            _mesh.ClearSurfaces();
            return !Stopping;
        }

        BuildTrail(keep);
        return true;
    }

    /// <summary>Reads the followed ends. True when either moved.</summary>
    private bool ResolveEnds()
    {
        bool moved = false;
        if (_followsFrom)
        {
            if (_fromAnchor.TryResolve(out Vector3 from))
            {
                moved |= from.DistanceSquaredTo(_spec.From) > 0.000025f;
                _spec.From = from;
            }
            else
            {
                _followsFrom = false;
            }
        }

        if (_followsTo)
        {
            if (_toAnchor.TryResolve(out Vector3 to))
            {
                moved |= to.DistanceSquaredTo(_spec.To) > 0.000025f;
                _spec.To = to;
            }
            else
            {
                _followsTo = false;
            }
        }

        return moved;
    }

    private void Rebuild()
    {
        int segments = Mathf.Max(2, _spec.Segments);
        float jitter = _spec.Jitter;
        float width = Mathf.Max(0.005f, _spec.Width);
        if (_electric)
        {
            // Twice the kinks, and a sway bounded in metres: jagged, not bent.
            float length = Mathf.Max(0.01f, _spec.From.DistanceTo(_spec.To));
            float sway = _spec.Mode == VfxBoltMode.Lightning ? StrikeSway : BeamSway;
            segments = Mathf.Min(MaxSegments, segments * 2);
            jitter = Mathf.Min(jitter, VfxBoltPath.JitterFor(length, sway));
            width *= ElectricWidth;
        }

        VfxBoltPath.Generate(_spec.From, _spec.To, segments, jitter, _seed, _points);
        _branchPoints.Clear();
        _branchStarts.Clear();
        if (_spec.Branches > 0 && _spec.Mode == VfxBoltMode.Lightning)
        {
            VfxBoltPath.Branches(
                _points, _spec.Branches, Mathf.Max(2, segments / 3), jitter * 1.4f, _seed, _branchPoints,
                _branchStarts, _scratch);
        }

        _mesh.ClearSurfaces();
        if (_points.Count < 2)
        {
            return;
        }

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        if (_electric)
        {
            Strip(_points, 0, _points.Count, width * ElectricFringe, ElectricFringeAlpha, taperTip: false);
        }
        else
        {
            Strip(_points, 0, _points.Count, width * HaloWidth, HaloAlpha, taperTip: false);
        }

        Strip(_points, 0, _points.Count, width, 1f, taperTip: false);
        for (int b = 0; b < _branchStarts.Count; b++)
        {
            int start = _branchStarts[b];
            int end = b + 1 < _branchStarts.Count ? _branchStarts[b + 1] : _branchPoints.Count;
            Strip(_branchPoints, start, end - start, width * 0.7f, 0.85f, taperTip: true);
        }

        if (_electric)
        {
            // Thinner strands between the same two ends, each on its own shape.
            int strands = Mathf.Clamp(VfxQuality.Rich.BoltStrands, 1, 4);
            for (int s = 1; s < strands; s++)
            {
                VfxBoltPath.Generate(
                    _spec.From, _spec.To, segments, jitter * 1.5f, unchecked(_seed + (s * 104729)), _scratch);
                Strip(_scratch, 0, _scratch.Count, width * 0.55f, 0.6f, taperTip: false);
            }
        }

        _mesh.SurfaceEnd();
    }

    private void BuildTrail(float keep)
    {
        float width = Mathf.Max(0.005f, _spec.Width);
        _points.Clear();
        for (int i = 0; i < _history.Count; i++)
        {
            _points.Add(_history[i].Position);
        }

        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        int count = _points.Count;
        for (int pass = 0; pass < 2; pass++)
        {
            float passWidth = pass == 0 ? width * 2.6f : width;
            float passAlpha = pass == 0 ? HaloAlpha : 1f;
            for (int i = 0; i < count - 1; i++)
            {
                // Oldest first: the tail is thin and faint, the head is the full line.
                float a0 = 1f - Mathf.Clamp((float)(Age - _history[i].At) / keep, 0f, 1f);
                float a1 = 1f - Mathf.Clamp((float)(Age - _history[i + 1].At) / keep, 0f, 1f);
                Quad(
                    _points[i], _points[i + 1], Tangent(_points, 0, count, i), Tangent(_points, 0, count, i + 1),
                    passWidth * (0.25f + (0.75f * a0)), passWidth * (0.25f + (0.75f * a1)),
                    a0 * passAlpha, a1 * passAlpha, (float)i / count, (float)(i + 1) / count);
            }
        }

        _mesh.SurfaceEnd();
        _material.SetShaderParameter(VfxMaterials.Opacity, 1f);
    }

    private void Strip(List<Vector3> points, int start, int count, float width, float alpha, bool taperTip)
    {
        for (int i = 0; i < count - 1; i++)
        {
            float u0 = (float)i / (count - 1);
            float u1 = (float)(i + 1) / (count - 1);
            float a0 = alpha * (taperTip ? 1f - u0 : 1f);
            float a1 = alpha * (taperTip ? 1f - u1 : 1f);
            float w0 = width * (taperTip ? 1f - (0.7f * u0) : 1f);
            float w1 = width * (taperTip ? 1f - (0.7f * u1) : 1f);
            Quad(
                points[start + i], points[start + i + 1], Tangent(points, start, count, i),
                Tangent(points, start, count, i + 1), w0, w1, a0, a1, u0, u1);
        }
    }

    /// <summary>The direction of the line at point <paramref name="i"/> of a strip: across its two
    /// neighbours, so the ribbon bends at a kink instead of pinching.</summary>
    private static Vector3 Tangent(List<Vector3> points, int start, int count, int i)
    {
        Vector3 before = points[start + Mathf.Max(0, i - 1)];
        Vector3 after = points[start + Mathf.Min(count - 1, i + 1)];
        Vector3 along = after - before;
        return along.LengthSquared() < 0.0000001f ? Vector3.Up : along.Normalized();
    }

    private void Quad(
        Vector3 a, Vector3 b, Vector3 tangentA, Vector3 tangentB, float widthA, float widthB, float alphaA,
        float alphaB, float uA, float uB)
    {
        var colourA = new Color(1f, 1f, 1f, alphaA);
        var colourB = new Color(1f, 1f, 1f, alphaB);
        Vertex(a, tangentA, uA, 0f, widthA, colourA);
        Vertex(a, tangentA, uA, 1f, widthA, colourA);
        Vertex(b, tangentB, uB, 0f, widthB, colourB);
        Vertex(b, tangentB, uB, 0f, widthB, colourB);
        Vertex(a, tangentA, uA, 1f, widthA, colourA);
        Vertex(b, tangentB, uB, 1f, widthB, colourB);
    }

    private void Vertex(Vector3 position, Vector3 tangent, float u, float side, float width, Color colour)
    {
        _mesh.SurfaceSetNormal(tangent);
        _mesh.SurfaceSetUV(new Vector2(u, side));
        _mesh.SurfaceSetUV2(new Vector2(width, 0f));
        _mesh.SurfaceSetColor(colour);
        _mesh.SurfaceAddVertex(position);
    }
}
