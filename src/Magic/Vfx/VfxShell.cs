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

    /// <summary>How tightly a rim-lit shell's light hugs its outline: higher is a thinner rim and a
    /// clearer middle. 0 = the default.</summary>
    public float RimPower;

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

    /// <summary>The body of a blast: its outline is lumpy, it is a mass of flame with burning edges
    /// rather than a filled ball, and it starts already torn open. Set by <see cref="Ball"/>.</summary>
    public bool Ragged;

    /// <summary>A ragged one-shot cools as it ages: what is left of it turns dark and covers what
    /// is behind it. Fire becoming smoke.</summary>
    public bool Cools;

    /// <summary>Drawn as ice (<c>vfx_ice.gdshader</c>): translucent blue plates with bright seams, a
    /// lit rim and, on a sheet, a jagged crystal crest. <see cref="Occlude"/> is how much the ice
    /// covers what is behind it. Set by <see cref="IceWall"/> and <see cref="IceShell"/>.</summary>
    public bool Ice;

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

    /// <summary>The ball of a blast of <paramref name="radius"/> metres: an eroding, lumpy body
    /// of the school's fire that swells, burns away and (with <paramref name="cools"/>) turns to
    /// smoke. Opacity is bounded by the coverage governor.</summary>
    public static VfxShellSpec Ball(Vector3 position, float radius, in VfxSchoolColors colors, bool cools)
    {
        VfxShellSpec ball = Sphere(position, radius, colors);
        ball.BurnsAway = true;
        ball.StartScale = 0.35f;
        ball.Layered = true;
        ball.Ragged = true;
        ball.Cools = cools;
        ball.Scroll = new Vector2(0.08f, 0.5f);
        ball.Tiling = new Vector2(3f, 1.5f);
        return ball;
    }

    /// <summary>A wall of ice, <paramref name="width"/> by <paramref name="height"/> metres.</summary>
    public static VfxShellSpec IceWall(Vector3 position, float width, float height, in VfxSchoolColors colors)
    {
        VfxShellSpec wall = Sheet(position, width, height, colors);
        wall.Ice = true;
        wall.Occlude = 0.6f;
        wall.FadeTop = 0f;
        wall.FadeSides = 0f;
        wall.Tiling = new Vector2(Mathf.Max(1f, width * 0.45f), Mathf.Max(1f, height * 0.45f));
        wall.Energy = 0.5f;
        wall.RiseSeconds = 0.4f;
        return wall;
    }

    /// <summary>A shell of ice closed over a body.</summary>
    public static VfxShellSpec IceShell(Vector3 position, float radius, in VfxSchoolColors colors)
    {
        VfxShellSpec shell = Sphere(position, radius, colors);
        shell.Ice = true;
        shell.Fresnel = true;
        shell.Occlude = 0.3f;
        shell.Energy = 0.5f;
        shell.Tiling = new Vector2(3f, 2f);
        return shell;
    }

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
/// fresnel shell of a ward, the body of a heavy bolt, a wall of flame; or, drawn as ice
/// (<c>vfx_ice.gdshader</c>), a wall of ice or a frozen shell.
/// One-shot (it swells, burns away and is gone) or sustained (it stands until stopped).
///
/// <para>A body of fire is governed (<see cref="VfxCoverageRules"/>): its opacity is cut by how much
/// of the frame it covers, so a blast near the camera is a mass of flame seen through, never a
/// solid ball across the screen. Rim-lit shells, walls and posts are not: they are thin or they are
/// gameplay objects.</para>
/// </summary>
public partial class VfxShell : VfxEffect
{
    private readonly MeshInstance3D _outer;
    private readonly MeshInstance3D _inner;
    private readonly ShaderMaterial _outerMaterial;
    private readonly ShaderMaterial _innerMaterial;
    private readonly ShaderMaterial _outerIce;
    private readonly ShaderMaterial _innerIce;

    private VfxShellSpec _spec;

    public VfxShell()
    {
        _outerMaterial = VfxMaterials.FlowMaterial();
        _innerMaterial = VfxMaterials.FlowMaterial();
        _innerMaterial.SetShaderParameter(VfxMaterials.TimeOffset, 3.7f);
        _outerIce = VfxMaterials.Ice();
        _innerIce = VfxMaterials.Ice();
        _innerIce.SetShaderParameter(VfxMaterials.TimeOffset, 3.7f);
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
        _outer.MaterialOverride = spec.Ice ? _outerIce : _outerMaterial;
        _inner.MaterialOverride = spec.Ice ? _innerIce : _innerMaterial;

        // Two sheets a hand apart, so a wall has depth from the side and the flames cross. A post's
        // mesh is centred, so it is lifted to stand on its base.
        _outer.Position = sheet && spec.Layered
            ? new Vector3(0f, 0f, 0.12f)
            : post ? new Vector3(0f, 0.45f * spec.Size.Y, 0f) : Vector3.Zero;
        _inner.Position = sheet ? new Vector3(0f, 0f, -0.12f) : Vector3.Zero;

        VfxSchoolColors colors = spec.Colors;
        float energy = colors.MidEnergy * (spec.Energy <= 0f ? 1f : spec.Energy);
        if (spec.Ice)
        {
            StyleIce(_outerIce, spec, colors);
            StyleIce(_innerIce, spec, colors);
            Apply(0f);
            return;
        }

        float soft = VfxQuality.Rich.SoftParticles ? 0.35f : 0f;
        Style(_outerMaterial, spec, colors.Core, colors.Edge, energy, soft, 1f);
        // The inner layer is the hot one: core colour all the way through, and brighter.
        Style(_innerMaterial, spec, colors.Core, sheet ? colors.Mid : colors.Core, sheet ? energy : colors.CoreEnergy, soft, 1.6f);
        Color dark = new Color(0.03f, 0.028f, 0.026f).Lerp(colors.Edge, 0.08f);
        _outerMaterial.SetShaderParameter(VfxMaterials.ColorDark, dark);
        _innerMaterial.SetShaderParameter(VfxMaterials.ColorDark, dark);

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
        float cool = 0f;
        bool body = _spec.Shape == VfxShellShape.Sphere && !_spec.Fresnel && !_spec.Ice;

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

            // A standing body of fire (a bolt's, a falling rock's) is torn, never a filled ball.
            if (_spec.Ragged && !_spec.Ice)
            {
                erode = Mathf.Max(erode, 0.3f);
            }
        }
        else
        {
            float start = Mathf.Clamp(_spec.StartScale <= 0f ? 1f : _spec.StartScale, 0.02f, 1f);
            float eased = 1f - ((1f - t) * (1f - t) * (1f - t));
            size *= Mathf.Lerp(start, 1f, eased);
            opacity *= 1f - (t * t);
            if (_spec.Ragged)
            {
                // Torn open from its first frame, and eaten to nothing by its last.
                erode = Mathf.Lerp(0.36f, 0.97f, Mathf.SmoothStep(0.05f, 1f, t));
                cool = _spec.Cools ? Mathf.SmoothStep(0.3f, 0.85f, t) : 0f;
            }
            else
            {
                erode = _spec.BurnsAway ? Mathf.SmoothStep(0.15f, 1f, t) * 0.85f : 0f;
            }
        }

        if (body && Director is { HasCamera: true } eye)
        {
            // The governor: a ball of fire is thinned by how much of the frame it covers.
            float largest = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 0.5f;
            opacity *= VfxCoverageRules.Opacity(
                largest * 0.8f, eye.DistanceToCamera(GlobalPosition), VfxQuality.Tier, _spec.Sustain ? 1d : Age);
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
        if (_spec.Ice)
        {
            // Ice does not burn away: it shatters, plate by plate, when it is let go.
            float shatter = _spec.Sustain ? erode * 1.2f : (_spec.BurnsAway ? Mathf.SmoothStep(0.35f, 1f, erode / 0.85f) * 1.2f : 0f);
            _outerIce.SetShaderParameter(VfxMaterials.Opacity, opacity);
            _outerIce.SetShaderParameter(VfxMaterials.Erode, shatter);
            _innerIce.SetShaderParameter(VfxMaterials.Opacity, opacity * 0.8f);
            _innerIce.SetShaderParameter(VfxMaterials.Erode, shatter);
            return;
        }

        _outerMaterial.SetShaderParameter(VfxMaterials.Opacity, opacity);
        _outerMaterial.SetShaderParameter(VfxMaterials.Erode, erode);
        _outerMaterial.SetShaderParameter(VfxMaterials.Cool, cool);
        _innerMaterial.SetShaderParameter(VfxMaterials.Opacity, opacity);
        _innerMaterial.SetShaderParameter(VfxMaterials.Erode, erode * 0.8f);
        _innerMaterial.SetShaderParameter(VfxMaterials.Cool, cool);
    }

    private static void StyleIce(ShaderMaterial material, in VfxShellSpec spec, in VfxSchoolColors colors)
    {
        bool sheet = spec.Shape == VfxShellShape.Sheet;
        material.SetShaderParameter(VfxMaterials.ColorCore, colors.Core);
        material.SetShaderParameter(VfxMaterials.ColorBody, colors.Mid.Darkened(0.25f));
        material.SetShaderParameter(VfxMaterials.ColorDeep, colors.Edge.Darkened(0.72f));
        material.SetShaderParameter(VfxMaterials.Energy, colors.MidEnergy * (spec.Energy <= 0f ? 0.5f : spec.Energy));
        material.SetShaderParameter(VfxMaterials.Occlude, Mathf.Clamp(spec.Occlude, 0f, 1f));
        material.SetShaderParameter(VfxMaterials.Tiling, spec.Tiling == Vector2.Zero ? Vector2.One : spec.Tiling);
        material.SetShaderParameter(VfxMaterials.SheetMode, sheet ? 1f : 0f);
        material.SetShaderParameter(VfxMaterials.Crest, sheet ? 0.26f : 0f);
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
        material.SetShaderParameter(VfxMaterials.FresnelPower, spec.RimPower > 0f ? spec.RimPower : 2.2f);
        material.SetShaderParameter(VfxMaterials.FadeTop, spec.FadeTop);
        material.SetShaderParameter(VfxMaterials.FadeSides, spec.FadeSides);
        material.SetShaderParameter(
            VfxMaterials.EdgeSoft,
            spec.Shape == VfxShellShape.Sphere ? 1f : spec.Shape == VfxShellShape.Sheet ? 0.35f : 0f);
        material.SetShaderParameter(VfxMaterials.SoftDepth, soft);

        // A blast's body, as opposed to a wall or a ward: see the uniforms in vfx_flow.gdshader.
        bool ragged = spec.Ragged && spec.Shape == VfxShellShape.Sphere;
        material.SetShaderParameter(VfxMaterials.Billow, ragged ? 0.14f : 0f);
        material.SetShaderParameter(VfxMaterials.Front, ragged ? 0.85f : 0f);

        // A sheet of fire is thinned where it is cooler too, so it is tongues of flame with the
        // world showing between them and not a lit panel.
        bool flames = spec.Shape == VfxShellShape.Sheet && !spec.Fresnel;
        material.SetShaderParameter(VfxMaterials.Thin, ragged ? 0.7f : flames ? 0.55f : 0f);
        material.SetShaderParameter(VfxMaterials.Cool, 0f);
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
        VfxMaterials.OnLayer(instance);
        AddChild(instance);
        return instance;
    }
}
