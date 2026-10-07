using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The ground marking that warns a wind-up is coming (Phase 36C, with telegraph classes): a flat shape
/// at the attacker's feet that grows from nothing to full over the wind-up and vanishes when the
/// window closes. Built the way <see cref="ImpactEffect"/> is — mesh and material once, re-armed per
/// use — but it lives for as long as it is told to rather than a fixed lifetime, because the thing it
/// is warning about does too.
///
/// <para>What it looks like says what to do, by <em>shape</em> as well as colour (so it reads for a
/// colour-blind player): a plain ring is an ordinary blow; a ring with a gold ring closing inside it is
/// a parryable strike, and it whitens for the last beat when the guard should go up; a thick filled,
/// pulsing ring is "do not guard, get out"; a fan is a wide sweep and shows which arc is dangerous.</para>
///
/// <para><b>How it is inked.</b> Each shape is a flat mesh (<see cref="Arc"/>) drawn by
/// <c>assets/shaders/fx/telegraph.gdshader</c>: a see-through body at about a third opacity, a brighter
/// rim that shimmers, a lit part that sweeps to the outer edge as the blow arrives, and edges that fade
/// out. The rim carries the class colour (what to do); the body carries the school of the spell being
/// wound up when the owner knows it (<see cref="Tint"/>). High Contrast draws a flat solid body with a
/// hard rim, and Reduced Motion holds the shimmer and the unblockable pulse still. The footprint and
/// the timing are the mesh and <see cref="TelegraphMath"/>, exactly as before: only the ink changed.</para>
///
/// <b>Model-independent by construction.</b> The other telegraph in the game is an emissive flare
/// on the body's material, which only exists if a creature has an authored model; the three dragons
/// greybox from their hit zones and so flashed nothing at all. A ring is drawn from geometry this
/// class owns, so it works on a greybox, a capsule and a finished model alike — and it reads from
/// above, which a body flare does not.
/// </summary>
public partial class TelegraphRing : Node3D
{
    /// <summary>How far above the feet the shape sits, so it does not z-fight with the ground.</summary>
    private const float Lift = 0.05f;

    /// <summary>How far above the feet a ring sits. Higher than a fan: the ring used to be a tube that
    /// stood this far out of the ground, which is what kept it in sight on a bank or over a rut, and
    /// a flat band on <see cref="Lift"/> would be half buried there.</summary>
    private const float RingLift = 0.14f;

    /// <summary>Inner radius of an ordinary ring, as a fraction of the outer.</summary>
    private const float StandardInner = 0.78f;

    /// <summary>Inner radius of a fan, as a fraction of the outer.</summary>
    private const float FanInner = 0.25f;

    /// <summary>Where the thick rim of an unblockable starts, as a fraction of the radius.</summary>
    private const float UnblockableInner = 0.55f;

    private const string ShaderPath = "res://assets/shaders/fx/telegraph.gdshader";

    private static readonly Color TimingColor = new(1.0f, 0.82f, 0.35f);
    private static readonly Color CueColor = new(1.0f, 0.97f, 0.85f);
    private static readonly Color DodgeColor = new(1.0f, 0.15f, 0.05f);

    private static readonly StringName FillColorParam = "fill_color";
    private static readonly StringName RimColorParam = "rim_color";
    private static readonly StringName FillAlphaParam = "fill_alpha";
    private static readonly StringName RimAlphaParam = "rim_alpha";
    private static readonly StringName RimWidthParam = "rim_width";
    private static readonly StringName ProgressParam = "progress";
    private static readonly StringName InnerEdgeParam = "inner_edge";
    private static readonly StringName SideEdgeParam = "side_edge";
    private static readonly StringName MotionParam = "motion";
    private static readonly StringName PlainParam = "plain";

    // The shape meshes and the shader, built once and shared by every ring. Arm used to allocate a
    // new mesh, and so generate and upload its geometry, on every telegraphed wind-up.
    private static readonly Dictionary<int, ArrayMesh> ArcCache = new();
    private static Shader? _shader;
    private static bool _shaderTried;

    private readonly MeshInstance3D _shape;
    private readonly Material _shapeMaterial;
    private readonly MeshInstance3D _timing;
    private readonly Material _timingMaterial;
    private float _radius = 1f;
    private double _duration;
    private double _age;
    private bool _active;
    private TelegraphClass _class;
    private float _parryWindow = 0.2f;
    private Color _base = Colors.White;
    private Color _tint = Colors.White;
    private bool _highContrast;
    private bool _motion = true;

    /// <summary>
    /// Builds the geometry in the constructor rather than in <c>_Ready</c>, so the ring is usable the
    /// moment it exists.
    ///
    /// ⚠️ This is not a style preference. The owner adds this node with <c>CallDeferred</c> (it has to —
    /// the body is mid-child-setup during the component's <c>_Ready</c>), so there is a window of up to
    /// one frame where the ring is alive but not yet in the tree and <c>_Ready</c> has not run. With the
    /// mesh and material built here instead, an <see cref="Arm"/> landing in that window is a no-op that
    /// draws nothing rather than a <c>NullReferenceException</c> — and a future caller who forgets to
    /// defer gets an invisible ring instead of a crash on every swing.
    /// </summary>
    public TelegraphRing()
    {
        // The one shape of the warning: a ring, a filled disc with a thick rim, or a fan. A band
        // reads as a ring rather than a puddle, and leaves the creature visible inside it.
        _shapeMaterial = MakeMaterial();
        _shape = new MeshInstance3D
        {
            Mesh = Arc(360f, StandardInner),
            MaterialOverride = _shapeMaterial,
            Position = new Vector3(0f, RingLift, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        // Parryable: a thin gold ring that closes on the outer one, so contact is when it reaches the
        // middle.
        _timingMaterial = MakeMaterial();
        _timing = new MeshInstance3D
        {
            Mesh = Arc(360f, 0.9f),
            MaterialOverride = _timingMaterial,
            Position = new Vector3(0f, RingLift + 0.01f, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>The class of the warning on screen (Standard when none).</summary>
    public TelegraphClass Class => _class;

    /// <summary>Whether the "raise your guard now" cue is lit this frame.</summary>
    public bool CueLit { get; private set; }

    /// <summary>True while a warning is on screen — the component uses this to avoid re-arming a
    /// ring that is already running for the same swing.</summary>
    public bool IsActive => _active;

    /// <summary>The telegraph shader's material, or (should the shader ever fail to load) a plain
    /// see-through one on the same mesh: a hard-edged warning is still a warning.</summary>
    private static Material MakeMaterial()
    {
        if (!_shaderTried)
        {
            _shaderTried = true;
            _shader = ResourceLoader.Exists(ShaderPath) ? GD.Load<Shader>(ShaderPath) : null;
        }

        if (_shader != null && IsInstanceValid(_shader))
        {
            return new ShaderMaterial { Shader = _shader };
        }

        return new StandardMaterial3D
        {
            // Drawn on top of the ground rather than fighting it for depth, and visible from either
            // side so a camera below the shape still sees the warning.
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    /// <summary>A flat annular sector spanning <paramref name="degrees"/> centred on forward (-Z),
    /// from <paramref name="inner"/> of the radius out to the full radius (360 is a closed ring, an
    /// inner radius of 0 a disc). UV.x runs 0 at the inner edge to 1 at the outer and UV.y 0..1 along
    /// the arc, which is all the shader reads. Cached per angle and inner radius.</summary>
    public static ArrayMesh Arc(float degrees, float inner)
    {
        int angle = (int)Mathf.Clamp(Mathf.Round(degrees), 10f, 360f);
        int hole = (int)Mathf.Clamp(Mathf.Round(inner * 100f), 0f, 95f);
        int key = (angle * 100) + hole;
        if (ArcCache.TryGetValue(key, out ArrayMesh? cached) && IsInstanceValid(cached))
        {
            return cached;
        }

        int segments = Mathf.Clamp(angle / 5, 12, 72);
        float half = Mathf.DegToRad(angle) * 0.5f;
        float innerRadius = hole / 100f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < segments; i++)
        {
            float u0 = (float)i / segments;
            float u1 = (float)(i + 1) / segments;
            float a0 = -half + (half * 2f * u0);
            float a1 = -half + (half * 2f * u1);
            Vector3 d0 = new(Mathf.Sin(a0), 0f, -Mathf.Cos(a0));
            Vector3 d1 = new(Mathf.Sin(a1), 0f, -Mathf.Cos(a1));
            st.SetUV(new Vector2(0f, u0));
            st.AddVertex(d0 * innerRadius);
            st.SetUV(new Vector2(1f, u0));
            st.AddVertex(d0);
            st.SetUV(new Vector2(1f, u1));
            st.AddVertex(d1);
            if (hole > 0)
            {
                st.SetUV(new Vector2(0f, u0));
                st.AddVertex(d0 * innerRadius);
                st.SetUV(new Vector2(1f, u1));
                st.AddVertex(d1);
                st.SetUV(new Vector2(0f, u1));
                st.AddVertex(d1 * innerRadius);
            }
        }

        ArrayMesh mesh = st.Commit();
        ArcCache[key] = mesh;
        return mesh;
    }

    public override void _Ready()
    {
        AddChild(_shape);
        AddChild(_timing);

        // Every actor that can telegraph carries one of these for its whole life and it is idle
        // between wind-ups, so it is only called while armed. The ring can be armed before it
        // reaches the tree (see the constructor), hence _active rather than a flat false.
        SetProcess(_active);
    }

    /// <summary>
    /// (Re)arms a plain ring for a wind-up of <paramref name="seconds"/>, tinted <paramref name="color"/>
    /// and sized to <paramref name="radius"/> metres. A non-positive duration is a wind-up too short
    /// to warn about, and is ignored rather than flashing a single frame.
    /// </summary>
    public void Arm(float seconds, float radius, Color color) =>
        Arm(seconds, radius, color, TelegraphClass.Standard);

    /// <summary>
    /// Arms the warning as <paramref name="cls"/>: a plain ring, a ring with a closing gold timing
    /// ring and a parry cue, a filled pulsing ring, or a fan of <paramref name="sweepDegrees"/>.
    /// <paramref name="parryWindow"/> is the defender's parry window, which sets when the cue lights
    /// (<see cref="TelegraphMath.ParryCueStart"/>). The duration is always the real wind-up.
    /// </summary>
    public void Arm(float seconds, float radius, Color color, TelegraphClass cls,
        float parryWindow = 0.2f, float sweepDegrees = TelegraphClasses.DefaultSweepDegrees)
    {
        if (seconds <= 0f)
        {
            return;
        }

        _duration = seconds;
        _radius = Mathf.Max(0.1f, radius);
        _age = 0d;
        _active = true;
        _class = cls;
        _parryWindow = parryWindow;
        _base = cls == TelegraphClass.Unblockable ? DodgeColor : color;
        _tint = color;
        CueLit = false;

        // Read once per warning: the two accessibility switches that change how it is inked.
        _highContrast = false;
        _motion = true;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings))
        {
            _highContrast = settings.Current.HighContrast;
            _motion = !settings.Current.ReducedMotion;
        }

        bool fan = cls == TelegraphClass.Sweep;
        bool disc = cls == TelegraphClass.Unblockable;
        _shape.Mesh = fan ? Arc(sweepDegrees, FanInner) : Arc(360f, disc ? 0f : StandardInner);
        _shape.Position = new Vector3(0f, fan ? Lift : RingLift, 0f);
        _shape.Visible = true;
        _timing.Visible = cls == TelegraphClass.Parryable;

        // What does not change over the wind-up. An unblockable is a filled disc whose thick rim is
        // the thick ring; a fan has lit sides; a plain ring is mostly rim.
        if (_shapeMaterial is ShaderMaterial shape)
        {
            shape.SetShaderParameter(RimWidthParam, disc ? 1f - UnblockableInner : fan ? 0.1f : 0.55f);
            shape.SetShaderParameter(InnerEdgeParam, disc ? 0f : 1f);
            shape.SetShaderParameter(SideEdgeParam, fan && sweepDegrees < 359f ? 1f : 0f);
            shape.SetShaderParameter(MotionParam, _motion ? 1f : 0f);
            shape.SetShaderParameter(PlainParam, _highContrast ? 1f : 0f);
        }

        if (_timingMaterial is ShaderMaterial timing)
        {
            timing.SetShaderParameter(RimWidthParam, 1f);
            timing.SetShaderParameter(MotionParam, 0f);
            timing.SetShaderParameter(PlainParam, _highContrast ? 1f : 0f);
        }

        Apply(0f);
        SetProcess(true);
    }

    /// <summary>Tints the body of the warning on screen with the school of what is being wound up
    /// (fire, frost, ash, arcane). The rim keeps the colour that says what to do about it. No effect
    /// when nothing is armed; the next <see cref="Arm"/> starts from its own colour again.</summary>
    public void Tint(Color school)
    {
        if (_active)
        {
            _tint = school;
        }
    }

    /// <summary>Ends the warning now — the window closed, or the wind-up was interrupted. Hiding it
    /// early is the whole feedback for a successful punish, so this is not merely cleanup.</summary>
    public void Clear()
    {
        _active = false;
        SetProcess(false);
        CueLit = false;
        _shape.Visible = false;
        _timing.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }

        _age += delta;
        float t = (float)(_age / _duration);
        if (t >= 1f)
        {
            Clear();
            return;
        }

        Apply(t);
    }

    /// <summary>Grows the shape and brightens it as the blow approaches, so how far along the wind-up
    /// is can be read off the warning itself rather than only from its presence.</summary>
    private void Apply(float t)
    {
        float scale = TelegraphMath.RingScale(t) * _radius;
        float pulse = TelegraphMath.ClassPulse(_class, (float)_age, _motion);
        float fillAlpha = TelegraphMath.FillAlpha(t, _highContrast) * pulse;
        float rimAlpha = TelegraphMath.RimAlpha(t, _highContrast) * pulse;

        // The cue: in the last beat before a parryable blow the ring whitens and goes solid, which is
        // the "guard now" flash. Timed off this wind-up's own length and the defender's parry window.
        bool cue = _class == TelegraphClass.Parryable &&
                   TelegraphMath.InParryCue(t, (float)_duration, _parryWindow);
        CueLit = cue;
        Color rim = cue ? CueColor : _base;
        Color fill = cue ? CueColor : _tint;
        if (cue)
        {
            fillAlpha = 0.75f;
            rimAlpha = 1f;
        }

        _shape.Scale = new Vector3(scale, 1f, scale);
        Ink(_shapeMaterial, fill, rim, fillAlpha, rimAlpha, t);

        if (_class == TelegraphClass.Parryable)
        {
            float inner = scale * TelegraphMath.TimingRingScale(t);
            _timing.Scale = new Vector3(inner, 1f, inner);
            Color timing = cue ? CueColor : TimingColor;
            float alpha = cue ? 1f : 0.85f;
            Ink(_timingMaterial, timing, timing, alpha, alpha, 1f);
        }
    }

    private static void Ink(Material material, Color fill, Color rim, float fillAlpha, float rimAlpha, float progress)
    {
        if (material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(FillColorParam, fill);
            shader.SetShaderParameter(RimColorParam, rim);
            shader.SetShaderParameter(FillAlphaParam, fillAlpha);
            shader.SetShaderParameter(RimAlphaParam, rimAlpha);
            shader.SetShaderParameter(ProgressParam, progress);
        }
        else if (material is StandardMaterial3D plain)
        {
            plain.AlbedoColor = new Color(rim.R, rim.G, rim.B, (fillAlpha + rimAlpha) * 0.5f);
        }
    }
}
