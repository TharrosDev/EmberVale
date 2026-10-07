using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The effect shaders and the handful of meshes every effect is drawn on, loaded once and shared.
///
/// <para>A pooled effect owns its <see cref="ShaderMaterial"/>s (built in its constructor from the
/// factory methods here) because each one animates its own colour, size and fade; what is shared is
/// the <see cref="Shader"/> behind them, which is what the renderer compiles. So there are seven
/// shaders to warm up however many effects are alive.</para>
///
/// <para><b>Every effect mesh is drawn on <see cref="RenderLayer"/> and on no other layer</b>
/// (<see cref="OnLayer"/>), and ground-mark decals are masked off that layer. A decal is applied to
/// every surface inside its box, transparent ones included, and an unshaded additive quad that has a
/// frost decal's pale colour mixed into its albedo is a translucent square hanging in the air: the
/// square edges of the first renders. Cameras see every layer, so nothing else changes.</para>
///
/// <para>Shader parameter names are <see cref="StringName"/>s held here, so setting one each frame
/// allocates nothing.</para>
/// </summary>
internal static class VfxMaterials
{
    public const string Folder = "res://assets/shaders/vfx/";

    public static readonly string[] ShaderNames =
    {
        "vfx_sprite", "vfx_flow", "vfx_ring", "vfx_ribbon", "vfx_distort", "vfx_ground", "vfx_ice",
    };

    /// <summary>The render layer (bit) every effect mesh is drawn on: layer 12. Decals leave it out.</summary>
    public const uint RenderLayer = 1u << 11;

    /// <summary>What a ground-mark decal projects onto: every layer but the effects' own.</summary>
    public const uint DecalMask = 0xFFFFFu & ~RenderLayer;

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
    public static readonly StringName Streaks = "streaks";
    public static readonly StringName UseHeat = "use_heat";
    public static readonly StringName TintCool = "tint_cool";
    public static readonly StringName OccludeCool = "occlude_cool";
    public static readonly StringName Dissolve = "dissolve";
    public static readonly StringName Billow = "billow";
    public static readonly StringName Front = "front";
    public static readonly StringName Thin = "thin";
    public static readonly StringName Cool = "cool";
    public static readonly StringName ColorDark = "color_dark";
    public static readonly StringName ColorBody = "color_body";
    public static readonly StringName ColorDeep = "color_deep";
    public static readonly StringName SheetMode = "sheet";
    public static readonly StringName Crest = "crest";
    public static readonly StringName MaxAngle = "max_angle";
    public static readonly StringName CoreWidth = "core_width";
    public static readonly StringName WhiteCore = "white_core";
    public static readonly StringName BodyOpacity = "body_opacity";
    public static readonly StringName PatternFloor = "pattern_floor";
    public static readonly StringName PatternSoft = "pattern_soft";
    public static readonly StringName Reveal = "reveal";
    public static readonly StringName SpanLimit = "span_limit";
    public static readonly StringName SpanFloor = "span_floor";

    /// <summary>Drawn before every other effect, so the rest of a blast lands on top of the bend.</summary>
    public const int DistortionPriority = -8;

    private static readonly Shader?[] Shaders = new Shader?[7];
    private static ArrayMesh? _annulus;
    private static ArrayMesh? _rimAnnulus;
    private static QuadMesh? _quad;
    private static QuadMesh? _streakQuad;
    private static QuadMesh? _sparkQuad;
    private static QuadMesh? _cometQuad;
    private static PlaneMesh? _plane;
    private static SphereMesh? _sphere;
    private static QuadMesh? _sheet;
    private static CylinderMesh? _post;

    /// <summary>
    /// Loads all seven shaders. False when any is missing, in which case the director draws nothing
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
            else if (Shaders[i]!.GetShaderUniformList().Count == 0)
            {
                // Every one of the seven declares uniforms, and a shader that failed to compile exposes
                // none. Drawing with it would hide the plain shapes behind effects that draw nothing
                // (an unseen bolt), so the whole layer steps aside and says why.
                Log.Warn($"Spell effects: shader {ShaderNames[i]} did not compile; spell effects are off.");
                all = false;
            }
        }

        return all;
    }

    /// <summary>A unit quad facing +Z: every sprite.</summary>
    public static QuadMesh Quad => _quad ??= new QuadMesh { Size = Vector2.One };

    /// <summary>A quad four times as long as it is wide: a spark, stretched along its travel.</summary>
    public static QuadMesh StreakQuad => _streakQuad ??= new QuadMesh { Size = new Vector2(0.28f, 1f) };

    /// <summary>A quad eight times as long as it is wide: a spark. Thinner than
    /// <see cref="StreakQuad"/>, which at a spark's size drew fat grains of rice.</summary>
    public static QuadMesh SparkQuad => _sparkQuad ??= new QuadMesh { Size = new Vector2(0.12f, 1f) };

    /// <summary>A quad twice as long as it is wide: a comet-shaped ember or wisp.</summary>
    public static QuadMesh CometQuad => _cometQuad ??= new QuadMesh { Size = new Vector2(0.5f, 1f) };

    /// <summary>A unit quad lying flat, facing up: rings and ground discs.</summary>
    public static PlaneMesh Plane => _plane ??= new PlaneMesh { Size = Vector2.One };

    /// <summary>The inner edge of <see cref="Annulus"/>, as a fraction of its outer radius.</summary>
    public const float AnnulusInner = 0.42f;

    /// <summary>
    /// A flat ring lying on the floor, facing up, with the UVs of <see cref="Plane"/>: the outer
    /// 58% of a unit disc. Shock rings and (on the lowest tier) a zone's rim are drawn on it, so the
    /// empty middle of a ring metres across is not rasterised at all.
    /// </summary>
    public static ArrayMesh Annulus => _annulus ??= BuildAnnulus(AnnulusInner, 40);

    /// <summary>The inner edge of <see cref="RimAnnulus"/>: just inside where <c>vfx_ground</c>
    /// draws a disc's rim (0.89 to 0.975 of the radius).</summary>
    public const float RimAnnulusInner = 0.84f;

    /// <summary>The outer band of a unit disc alone: a standing zone's rim on the leanest tier,
    /// which is under a third of the pixels of the whole floor.</summary>
    public static ArrayMesh RimAnnulus => _rimAnnulus ??= BuildAnnulus(RimAnnulusInner, 48);

    /// <summary>Puts an effect mesh on <see cref="RenderLayer"/> alone, out of reach of decals.</summary>
    public static void OnLayer(VisualInstance3D instance) => instance.Layers = RenderLayer;

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
        material.SetShaderParameter(Noise, VfxTextures.Noise);
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

    public static ShaderMaterial Ice()
    {
        ShaderMaterial material = New(6);
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
        _annulus = null;
        _rimAnnulus = null;
        _streakQuad = null;
        _sparkQuad = null;
        _cometQuad = null;
        _plane = null;
        _sphere = null;
        _sheet = null;
        _post = null;
    }

    private static ArrayMesh BuildAnnulus(float inner, int segments)
    {
        var vertices = new Vector3[segments * 2];
        var normals = new Vector3[segments * 2];
        var uvs = new Vector2[segments * 2];
        var indices = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            float x = Mathf.Cos(angle) * 0.5f;
            float z = Mathf.Sin(angle) * 0.5f;
            vertices[i * 2] = new Vector3(x * inner, 0f, z * inner);
            vertices[(i * 2) + 1] = new Vector3(x, 0f, z);
            normals[i * 2] = Vector3.Up;
            normals[(i * 2) + 1] = Vector3.Up;
            uvs[i * 2] = new Vector2((x * inner) + 0.5f, (z * inner) + 0.5f);
            uvs[(i * 2) + 1] = new Vector2(x + 0.5f, z + 0.5f);

            int next = (i + 1) % segments;
            int at = i * 6;
            indices[at] = i * 2;
            indices[at + 1] = (i * 2) + 1;
            indices[at + 2] = (next * 2) + 1;
            indices[at + 3] = i * 2;
            indices[at + 4] = (next * 2) + 1;
            indices[at + 5] = next * 2;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
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
