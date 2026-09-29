using System.Collections.Generic;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The ground marking that warns a wind-up is coming (Phase 36C, with telegraph classes): a flat disc
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
/// <b>Model-independent by construction.</b> The other telegraph in the game is an emissive flare
/// on the body's material, which only exists if a creature has an authored model; the three dragons
/// greybox from their hit zones and so flashed nothing at all. A ring is drawn from geometry this
/// class owns, so it works on a greybox, a capsule and a finished model alike — and it reads from
/// above, which a body flare does not.
/// </summary>
public partial class TelegraphRing : Node3D
{
    /// <summary>How far above the feet the disc sits, so it does not z-fight with the ground.</summary>
    private const float Lift = 0.05f;

    private static readonly Color TimingColor = new(1.0f, 0.82f, 0.35f);
    private static readonly Color CueColor = new(1.0f, 0.97f, 0.85f);
    private static readonly Color DodgeColor = new(1.0f, 0.15f, 0.05f);

    private static readonly Dictionary<int, ArrayMesh> SectorCache = new();

    private readonly MeshInstance3D _mesh;
    private readonly StandardMaterial3D _material;
    private readonly MeshInstance3D _timing;
    private readonly StandardMaterial3D _timingMaterial;
    private readonly MeshInstance3D _fill;
    private readonly StandardMaterial3D _fillMaterial;
    private readonly MeshInstance3D _fan;
    private readonly StandardMaterial3D _fanMaterial;
    private float _radius = 1f;
    private double _duration;
    private double _age;
    private bool _active;
    private TelegraphClass _class;
    private float _parryWindow = 0.2f;
    private Color _base = Colors.White;

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
        _material = MakeMaterial();
        _mesh = new MeshInstance3D
        {
            // A torus reads as a ring rather than a puddle, and leaves the creature visible inside it.
            Mesh = new TorusMesh { InnerRadius = 0.78f, OuterRadius = 1f, RingSegments = 32 },
            MaterialOverride = _material,
            Position = new Vector3(0f, Lift, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        // Parryable: a thin gold ring that closes on the outer one, so contact is when it reaches the
        // middle. Unblockable: a filled disc under a thick ring. Sweep: a fan in place of the ring.
        _timingMaterial = MakeMaterial();
        _timing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, RingSegments = 32 },
            MaterialOverride = _timingMaterial,
            Position = new Vector3(0f, Lift + 0.01f, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _fillMaterial = MakeMaterial();
        _fill = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.01f, RadialSegments = 32 },
            MaterialOverride = _fillMaterial,
            Position = new Vector3(0f, Lift - 0.01f, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _fanMaterial = MakeMaterial();
        _fan = new MeshInstance3D
        {
            MaterialOverride = _fanMaterial,
            Position = new Vector3(0f, Lift, 0f),
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

    private static StandardMaterial3D MakeMaterial() => new()
    {
        // Drawn on top of the ground rather than fighting it for depth, and visible from either
        // side so a camera below the disc still sees the warning.
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A flat annular sector spanning <paramref name="degrees"/> centred on forward (-Z),
    /// from a quarter of the radius out to the full radius. Cached per angle.</summary>
    public static ArrayMesh Sector(float degrees)
    {
        int key = (int)Mathf.Clamp(Mathf.Round(degrees), 10f, 360f);
        if (SectorCache.TryGetValue(key, out ArrayMesh? cached) && GodotObject.IsInstanceValid(cached))
        {
            return cached;
        }

        int segments = Mathf.Max(6, key / 8);
        float half = Mathf.DegToRad(key) * 0.5f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (int i = 0; i < segments; i++)
        {
            float a0 = -half + (half * 2f * i / segments);
            float a1 = -half + (half * 2f * (i + 1) / segments);
            Vector3 i0 = new(Mathf.Sin(a0) * 0.25f, 0f, -Mathf.Cos(a0) * 0.25f);
            Vector3 i1 = new(Mathf.Sin(a1) * 0.25f, 0f, -Mathf.Cos(a1) * 0.25f);
            Vector3 o0 = new(Mathf.Sin(a0), 0f, -Mathf.Cos(a0));
            Vector3 o1 = new(Mathf.Sin(a1), 0f, -Mathf.Cos(a1));
            st.AddVertex(i0);
            st.AddVertex(o0);
            st.AddVertex(o1);
            st.AddVertex(i0);
            st.AddVertex(o1);
            st.AddVertex(i1);
        }

        ArrayMesh mesh = st.Commit();
        SectorCache[key] = mesh;
        return mesh;
    }

    public override void _Ready()
    {
        AddChild(_mesh);
        AddChild(_timing);
        AddChild(_fill);
        AddChild(_fan);
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
        CueLit = false;

        bool fan = cls == TelegraphClass.Sweep;
        _mesh.Visible = !fan;
        _mesh.Mesh = cls == TelegraphClass.Unblockable
            ? new TorusMesh { InnerRadius = 0.55f, OuterRadius = 1f, RingSegments = 32 }
            : new TorusMesh { InnerRadius = 0.78f, OuterRadius = 1f, RingSegments = 32 };
        _timing.Visible = cls == TelegraphClass.Parryable;
        _fill.Visible = cls == TelegraphClass.Unblockable;
        _fan.Visible = fan;
        if (fan)
        {
            _fan.Mesh = Sector(sweepDegrees);
        }

        Apply(0f);
    }

    /// <summary>Ends the warning now — the window closed, or the wind-up was interrupted. Hiding it
    /// early is the whole feedback for a successful punish, so this is not merely cleanup.</summary>
    public void Clear()
    {
        _active = false;
        CueLit = false;
        _mesh.Visible = false;
        _timing.Visible = false;
        _fill.Visible = false;
        _fan.Visible = false;
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

    /// <summary>Grows the ring and brightens it as the blow approaches, so how far along the wind-up
    /// is can be read off the ring itself rather than only from its presence.</summary>
    private void Apply(float t)
    {
        float scale = TelegraphMath.RingScale(t) * _radius;
        float pulse = TelegraphMath.ClassPulse(_class, (float)_age);
        float alpha = TelegraphMath.RingAlpha(t) * pulse;

        // The cue: in the last beat before a parryable blow the ring whitens and goes solid, which is
        // the "guard now" flash. Timed off this wind-up's own length and the defender's parry window.
        bool cue = _class == TelegraphClass.Parryable &&
                   TelegraphMath.InParryCue(t, (float)_duration, _parryWindow);
        CueLit = cue;
        Color ring = cue ? CueColor : _base;
        if (cue)
        {
            alpha = 1f;
        }

        _material.Emission = ring;
        _material.AlbedoColor = new Color(ring.R, ring.G, ring.B, alpha);

        switch (_class)
        {
            case TelegraphClass.Sweep:
                _fan.Scale = new Vector3(scale, 1f, scale);
                _fanMaterial.Emission = _base;
                _fanMaterial.AlbedoColor = new Color(_base.R, _base.G, _base.B, alpha * 0.7f);
                break;

            case TelegraphClass.Unblockable:
                _mesh.Scale = new Vector3(scale, 1f, scale);
                _fill.Scale = new Vector3(scale, 1f, scale);
                _fillMaterial.Emission = _base;
                _fillMaterial.AlbedoColor = new Color(_base.R, _base.G, _base.B, 0.16f + (0.24f * t));
                break;

            case TelegraphClass.Parryable:
                _mesh.Scale = new Vector3(scale, 1f, scale);
                float inner = scale * TelegraphMath.TimingRingScale(t);
                _timing.Scale = new Vector3(inner, 1f, inner);
                Color timing = cue ? CueColor : TimingColor;
                _timingMaterial.Emission = timing;
                _timingMaterial.AlbedoColor = new Color(timing.R, timing.G, timing.B, cue ? 1f : 0.85f);
                break;

            default:
                _mesh.Scale = new Vector3(scale, 1f, scale);
                break;
        }
    }
}
