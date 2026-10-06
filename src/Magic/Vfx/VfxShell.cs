using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>The mesh a <see cref="VfxShell"/> flows over.</summary>
internal enum VfxShellShape
{
    /// <summary>A sphere: a ward, a frozen target, the ball of a blast, the body of a bolt.</summary>
    Sphere,

    /// <summary>An upright sheet standing on its base: a wall of fire or ice.</summary>
    Sheet,

    /// <summary>A solid tapering post standing on its base: a totem.</summary>
    Post,
}

/// <summary>What one <see cref="VfxShell"/> is asked to be.</summary>
internal struct VfxShellSpec
{
    public VfxShellShape Shape;
    public Vector3 Position;

    /// <summary>A sphere's diameters, or a sheet's width (X) and height (Y).</summary>
    public Vector3 Size;

    public VfxSchoolColors Colors;

    /// <summary>Seconds a one-shot lasts. Ignored when sustained.</summary>
    public float Life;

    /// <summary>Holds until stopped.</summary>
    public bool Sustain;

    /// <summary>Drawn as a rim-lit shell (a ward) rather than a body of fire.</summary>
    public bool Fresnel;

    /// <summary>0 = pure added light, 1 = covers what is behind it (ice).</summary>
    public float Occlude;

    /// <summary>A one-shot burns away from the inside as it ages, instead of only fading.</summary>
    public bool BurnsAway;

    /// <summary>The fraction of its size a one-shot starts at (it swells to full).</summary>
    public float StartScale;

    /// <summary>How the noise travels over the mesh, in UV a second.</summary>
    public Vector2 Scroll;

    public Vector2 Tiling;

    /// <summary>A sheet thins toward its top like flame (0..1).</summary>
    public float FadeTop;

    /// <summary>A sheet fades at its two ends (0..1).</summary>
    public float FadeSides;

    /// <summary>Emission against the school's mid energy.</summary>
    public float Energy;

    public float Opacity;

    /// <summary>A sheet climbs out of the ground over this many seconds.</summary>
    public float RiseSeconds;

    /// <summary>A second, smaller, hotter layer inside a sphere; a second sheet behind a sheet.</summary>
    public bool Layered;

    /// <summary>Turns the mesh so its local Z lies along this (a breath's body along its aim).
    /// Zero = unturned.</summary>
    public Vector3 Forward;

    public static VfxShellSpec Sphere(Vector3 position, float radius, in VfxSchoolColors colors) => new()
    {
        Shape = VfxShellShape.Sphere,
        Position = position,
        Size = Vector3.One * (radius * 2f),
        Colors = colors,
        Life = 0.4f,
        StartScale = 1f,
        Scroll = new Vector2(0.05f, 0.35f),
        Tiling = new Vector2(2f, 1f),
        Energy = 1f,
        Opacity = 1f,
    };

    public static VfxShellSpec Sheet(Vector3 position, float width, float height, in VfxSchoolColors colors) => new()
    {
        Shape = VfxShellShape.Sheet,
        Position = position,
        Size = new Vector3(width, height, 1f),
        Colors = colors,
        Life = 1f,
        Sustain = true,
        StartScale = 1f,
        Scroll = new Vector2(0.03f, 0.55f),
        Tiling = new Vector2(Mathf.Max(1f, width * 0.35f), 1f),
        FadeTop = 1f,
        FadeSides = 1f,
        Energy = 1f,
        Opacity = 1f,
        RiseSeconds = 0.3f,
        Layered = true,
    };
}

/// <summary>
/// A mesh with noise flowing over it (<c>vfx_flow.gdshader</c>): the ball of fire inside a blast, the
/// fresnel shell of a ward or a frozen target, the body of a heavy bolt, a wall of flame or ice.
/// One-shot (it swells, burns away and is gone) or sustained (it stands until stopped).
/// </summary>
public partial class VfxShell : VfxEffect
{
    private readonly MeshInstance3D _outer;
    private readonly MeshInstance3D _inner;
    private readonly ShaderMaterial _outerMaterial;
    private readonly ShaderMaterial _innerMaterial;

    private VfxShellSpec _spec;

    public VfxShell()
    {
        _outerMaterial = VfxMaterials.FlowMaterial();
        _innerMaterial = VfxMaterials.FlowMaterial();
        _innerMaterial.SetShaderParameter(VfxMaterials.TimeOffset, 3.7f);
        _outer = Layer("Outer", _outerMaterial);
        _inner = Layer("Inner", _innerMaterial);
    }

    /// <summary>Styles the shell for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxShellSpec spec)
    {
        _spec = spec;
        Basis facing = Basis.Identity;
        if (spec.Forward.LengthSquared() > 0.0001f)
        {
            Vector3 forward = spec.Forward.Normalized();
            facing = Basis.LookingAt(forward, Mathf.Abs(forward.Dot(Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up);
        }

        GlobalTransform = new Transform3D(facing, spec.Position);

        bool sheet = spec.Shape == VfxShellShape.Sheet;
        bool post = spec.Shape == VfxShellShape.Post;
        Mesh mesh = sheet ? VfxMaterials.Sheet : post ? VfxMaterials.Post : VfxMaterials.Sphere;
        _outer.Mesh = mesh;
        _inner.Mesh = mesh;
        _inner.Visible = spec.Layered && !post;

        // Two sheets a hand apart, so a wall has depth from the side and the flames cross. A post's
        // mesh is centred, so it is lifted to stand on its base.
        _outer.Position = sheet && spec.Layered
            ? new Vector3(0f, 0f, 0.12f)
            : post ? new Vector3(0f, 0.45f * spec.Size.Y, 0f) : Vector3.Zero;
        _inner.Position = sheet ? new Vector3(0f, 0f, -0.12f) : Vector3.Zero;

        VfxSchoolColors colors = spec.Colors;
        float energy = colors.MidEnergy * (spec.Energy <= 0f ? 1f : spec.Energy);
        float soft = VfxQuality.Tier == VfxTier.Performance ? 0f : 0.35f;
        Style(_outerMaterial, spec, colors.Core, colors.Edge, energy, soft, 1f);
        // The inner layer is the hot one: core colour all the way through, and brighter.
        Style(_innerMaterial, spec, colors.Core, sheet ? colors.Mid : colors.Core, sheet ? energy : colors.CoreEnergy, soft, 1.6f);

        Apply(0f);
    }

    protected override bool Tick(float delta)
    {
        if (_spec.Sustain)
        {
            if (Stopping && StopAge >= StopFadeSeconds * 2f)
            {
                return false;
            }

            Apply(0f);
            return true;
        }

        float t = (float)(Age / Mathf.Max(0.05f, _spec.Life));
        if (t >= 1f)
        {
            return false;
        }

        Apply(t);
        return true;
    }

    private void Apply(float t)
    {
        float opacity = _spec.Opacity <= 0f ? 1f : _spec.Opacity;
        Vector3 size = _spec.Size;
        float erode = 0f;

        if (_spec.Sustain)
        {
            opacity *= Mathf.Clamp((float)Age / 0.12f, 0f, 1f) * StopFade(StopFadeSeconds * 2f);
            if (_spec.RiseSeconds > 0f)
            {
                float rise = Mathf.Clamp((float)Age / _spec.RiseSeconds, 0f, 1f);
                size.Y *= 1f - ((1f - rise) * (1f - rise));
            }

            if (Stopping)
            {
                erode = 1f - StopFade(StopFadeSeconds * 2f);
            }
        }
        else
        {
            float start = Mathf.Clamp(_spec.StartScale <= 0f ? 1f : _spec.StartScale, 0.02f, 1f);
            float eased = 1f - ((1f - t) * (1f - t) * (1f - t));
            size *= Mathf.Lerp(start, 1f, eased);
            opacity *= 1f - (t * t);
            erode = _spec.BurnsAway ? Mathf.SmoothStep(0.15f, 1f, t) * 0.85f : 0f;
        }

        // Seen from inside, a sphere is its colour over the whole screen: a flash no cap covers. Judged
        // on its full size, so a ball that will swell past the eye is never shown at all.
        bool engulfs = _spec.Shape == VfxShellShape.Sphere && Director is { HasCamera: true } director &&
                       VfxScreenRules.Engulfs((director.CameraPosition - GlobalPosition) * GlobalBasis, _spec.Size * 0.5f);
        _outer.Visible = !engulfs;
        _inner.Visible = !engulfs && _spec.Layered && _spec.Shape != VfxShellShape.Post;

        size = new Vector3(Mathf.Max(0.01f, size.X), Mathf.Max(0.01f, size.Y), Mathf.Max(0.01f, size.Z));
        _outer.Scale = size;
        _inner.Scale = _spec.Shape == VfxShellShape.Sheet ? size : size * 0.62f;
        _outerMaterial.SetShaderParameter(VfxMaterials.Opacity, opacity);
        _outerMaterial.SetShaderParameter(VfxMaterials.Erode, erode);
        _innerMaterial.SetShaderParameter(VfxMaterials.Opacity, opacity);
        _innerMaterial.SetShaderParameter(VfxMaterials.Erode, erode * 0.8f);
    }

    private static void Style(
        ShaderMaterial material, in VfxShellSpec spec, Color core, Color edge, float energy, float soft, float speed)
    {
        material.SetShaderParameter(VfxMaterials.ColorCore, core);
        material.SetShaderParameter(VfxMaterials.ColorEdge, edge);
        material.SetShaderParameter(VfxMaterials.Energy, energy);
        material.SetShaderParameter(VfxMaterials.Occlude, Mathf.Clamp(spec.Occlude, 0f, 1f));
        material.SetShaderParameter(VfxMaterials.Scroll, spec.Scroll * speed);
        material.SetShaderParameter(VfxMaterials.Tiling, spec.Tiling == Vector2.Zero ? Vector2.One : spec.Tiling);
        material.SetShaderParameter(VfxMaterials.FresnelMix, spec.Fresnel ? 1f : 0f);
        material.SetShaderParameter(VfxMaterials.FresnelPower, 2.2f);
        material.SetShaderParameter(VfxMaterials.FadeTop, spec.FadeTop);
        material.SetShaderParameter(VfxMaterials.FadeSides, spec.FadeSides);
        material.SetShaderParameter(
            VfxMaterials.EdgeSoft,
            spec.Shape == VfxShellShape.Sphere ? 1f : spec.Shape == VfxShellShape.Sheet ? 0.35f : 0f);
        material.SetShaderParameter(VfxMaterials.SoftDepth, soft);
    }

    private MeshInstance3D Layer(string name, Material material)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = VfxMaterials.Sphere,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        AddChild(instance);
        return instance;
    }
}
