using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The six effect shaders and the handful of meshes every effect is drawn on, loaded once and shared.
///
/// <para>A pooled effect owns its <see cref="ShaderMaterial"/>s (built in its constructor from the
/// factory methods here) because each one animates its own colour, size and fade; what is shared is
/// the <see cref="Shader"/> behind them, which is what the renderer compiles. So there are six
/// shaders to warm up however many effects are alive.</para>
///
/// <para>Shader parameter names are <see cref="StringName"/>s held here, so setting one each frame
/// allocates nothing.</para>
/// </summary>
internal static class VfxMaterials
{
    public const string Folder = "res://assets/shaders/vfx/";

    public static readonly string[] ShaderNames =
    {
        "vfx_sprite", "vfx_flow", "vfx_ring", "vfx_ribbon", "vfx_distort", "vfx_ground",
    };

    // Parameter names, shared across the shaders that have them.
    public static readonly StringName Tint = "tint";
    public static readonly StringName Energy = "energy";
    public static readonly StringName Opacity = "opacity";
    public static readonly StringName Occlude = "occlude";
    public static readonly StringName HotCore = "hot_core";
    public static readonly StringName Spin = "spin";
    public static readonly StringName AlignVelocity = "align_velocity";
    public static readonly StringName SoftDepth = "soft_depth";
    public static readonly StringName Mask = "mask";
    public static readonly StringName Noise = "noise";
    public static readonly StringName ColorCore = "color_core";
    public static readonly StringName ColorEdge = "color_edge";
    public static readonly StringName Scroll = "scroll";
    public static readonly StringName Tiling = "tiling";
    public static readonly StringName TimeOffset = "time_offset";
    public static readonly StringName Erode = "erode";
    public static readonly StringName FresnelPower = "fresnel_power";
    public static readonly StringName FresnelMix = "fresnel_mix";
    public static readonly StringName FadeTop = "fade_top";
    public static readonly StringName FadeSides = "fade_sides";
    public static readonly StringName EdgeSoft = "edge_soft";
    public static readonly StringName Radius = "radius";
    public static readonly StringName Thickness = "thickness";
    public static readonly StringName Breakup = "breakup";
    public static readonly StringName InnerGlow = "inner_glow";
    public static readonly StringName WidthScale = "width_scale";
    public static readonly StringName FlowSpeed = "flow_speed";
    public static readonly StringName FlowAmount = "flow_amount";
    public static readonly StringName UvScale = "uv_scale";
    public static readonly StringName Strength = "strength";
    public static readonly StringName RimPower = "rim_power";
    public static readonly StringName Pattern = "pattern";
    public static readonly StringName Fill = "fill";
    public static readonly StringName Flow = "flow";
    public static readonly StringName PatternMix = "pattern_mix";
    public static readonly StringName Disc = "disc";
    public static readonly StringName Rim = "rim";

    /// <summary>Drawn before every other effect, so the rest of a blast lands on top of the bend.</summary>
    public const int DistortionPriority = -8;

    private static readonly Shader?[] Shaders = new Shader?[6];
    private static QuadMesh? _quad;
    private static QuadMesh? _streakQuad;
    private static PlaneMesh? _plane;
    private static SphereMesh? _sphere;
    private static QuadMesh? _sheet;
    private static CylinderMesh? _post;

    /// <summary>
    /// Loads all six shaders. False when any is missing, in which case the director draws nothing
    /// and every caller falls back to its plain shape: an effect with no shader is a white square.
    /// </summary>
    public static bool Load()
    {
        bool all = true;
        for (int i = 0; i < ShaderNames.Length; i++)
        {
            if (Shaders[i] == null || !GodotObject.IsInstanceValid(Shaders[i]))
            {
                Shaders[i] = GD.Load<Shader>(Folder + ShaderNames[i] + ".gdshader");
            }

            if (Shaders[i] == null)
            {
                Log.Warn($"Spell effects: shader {ShaderNames[i]} did not load; spell effects are off.");
                all = false;
            }
        }

        return all;
    }

    /// <summary>A unit quad facing +Z: every sprite.</summary>
    public static QuadMesh Quad => _quad ??= new QuadMesh { Size = Vector2.One };

    /// <summary>A quad four times as long as it is wide: a spark, stretched along its travel.</summary>
    public static QuadMesh StreakQuad => _streakQuad ??= new QuadMesh { Size = new Vector2(0.28f, 1f) };

    /// <summary>A unit quad lying flat, facing up: rings and ground discs.</summary>
    public static PlaneMesh Plane => _plane ??= new PlaneMesh { Size = Vector2.One };

    /// <summary>A unit-diameter sphere: shells, fireballs, distortion.</summary>
    public static SphereMesh Sphere =>
        _sphere ??= new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 24, Rings = 12 };

    /// <summary>A unit quad standing on its base (y from 0 to 1): a wall of fire or ice.</summary>
    public static QuadMesh Sheet =>
        _sheet ??= new QuadMesh { Size = Vector2.One, CenterOffset = new Vector3(0f, 0.5f, 0f) };

    /// <summary>A tapering post, 0.9 m tall and centred on its middle: a totem.</summary>
    public static CylinderMesh Post => _post ??= new CylinderMesh
    {
        TopRadius = 0.06f,
        BottomRadius = 0.13f,
        Height = 0.9f,
        RadialSegments = 10,
        Rings = 1,
    };

    public static ShaderMaterial Sprite(Texture2D mask, bool alignVelocity = false, bool occlude = false)
    {
        ShaderMaterial material = New(0);
        material.SetShaderParameter(Mask, mask);
        material.SetShaderParameter(AlignVelocity, alignVelocity ? 1f : 0f);
        material.SetShaderParameter(Occlude, occlude ? 1f : 0f);
        return material;
    }

    public static ShaderMaterial FlowMaterial()
    {
        ShaderMaterial material = New(1);
        material.SetShaderParameter(Noise, VfxTextures.Noise);
        return material;
    }

    public static ShaderMaterial Ring()
    {
        ShaderMaterial material = New(2);
        material.SetShaderParameter(Noise, VfxTextures.Noise);
        return material;
    }

    public static ShaderMaterial Ribbon()
    {
        ShaderMaterial material = New(3);
        material.SetShaderParameter(Noise, VfxTextures.Noise);
        return material;
    }

    public static ShaderMaterial Distort()
    {
        ShaderMaterial material = New(4);
        material.RenderPriority = DistortionPriority;
        return material;
    }

    public static ShaderMaterial Ground()
    {
        ShaderMaterial material = New(5);
        material.SetShaderParameter(Noise, VfxTextures.Noise);
        return material;
    }

    /// <summary>Drops the shaders and meshes. Called as the director leaves the tree.</summary>
    public static void Release()
    {
        for (int i = 0; i < Shaders.Length; i++)
        {
            Shaders[i] = null;
        }

        _quad = null;
        _streakQuad = null;
        _plane = null;
        _sphere = null;
        _sheet = null;
        _post = null;
    }

    private static ShaderMaterial New(int shader)
    {
        if (Shaders[shader] == null || !GodotObject.IsInstanceValid(Shaders[shader]))
        {
            Shaders[shader] = GD.Load<Shader>(Folder + ShaderNames[shader] + ".gdshader");
        }

        return new ShaderMaterial { Shader = Shaders[shader] };
    }
}
