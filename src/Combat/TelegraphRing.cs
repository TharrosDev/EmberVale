using Godot;

namespace Embervale.Combat;

/// <summary>
/// The ground ring that warns a wind-up is coming (Phase 36C): a flat disc at the attacker's feet
/// that grows from nothing to full over the wind-up and vanishes when the window closes. Built the
/// way <see cref="ImpactEffect"/> is — mesh and material once, re-armed per use — but it lives for
/// as long as it is told to rather than a fixed lifetime, because the thing it is warning about
/// does too.
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

    private readonly MeshInstance3D _mesh;
    private readonly StandardMaterial3D _material;
    private float _radius = 1f;
    private double _duration;
    private double _age;
    private bool _active;

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
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            // Drawn on top of the ground rather than fighting it for depth, and visible from either
            // side so a camera below the disc still sees the warning.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };

        _mesh = new MeshInstance3D
        {
            // A torus reads as a ring rather than a puddle, and leaves the creature visible inside it.
            Mesh = new TorusMesh { InnerRadius = 0.78f, OuterRadius = 1f, RingSegments = 32 },
            MaterialOverride = _material,
            Position = new Vector3(0f, Lift, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public override void _Ready()
    {
        AddChild(_mesh);
    }

    /// <summary>True while a warning is on screen — the component uses this to avoid re-arming a
    /// ring that is already running for the same swing.</summary>
    public bool IsActive => _active;

    /// <summary>
    /// (Re)arms the ring for a wind-up of <paramref name="seconds"/>, tinted <paramref name="color"/>
    /// and sized to <paramref name="radius"/> metres. A non-positive duration is a wind-up too short
    /// to warn about, and is ignored rather than flashing a single frame.
    /// </summary>
    public void Arm(float seconds, float radius, Color color)
    {
        if (seconds <= 0f)
        {
            return;
        }

        _duration = seconds;
        _radius = Mathf.Max(0.1f, radius);
        _age = 0d;
        _active = true;
        _material.Emission = color;
        _material.AlbedoColor = color;
        _mesh.Visible = true;
        Apply(0f);
    }

    /// <summary>Ends the warning now — the window closed, or the wind-up was interrupted. Hiding it
    /// early is the whole feedback for a successful punish, so this is not merely cleanup.</summary>
    public void Clear()
    {
        _active = false;
        _mesh.Visible = false;
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
        _mesh.Scale = new Vector3(scale, 1f, scale);

        Color color = _material.Emission;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, TelegraphMath.RingAlpha(t));
    }
}
