using System;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A short-lived expand-and-fade spark marking a combat impact (Phase 29C). Purely cosmetic — the damage
/// is already resolved. The melee/feedback analogue of <see cref="Magic.SpellFlash"/>, but <b>poolable</b>
/// (CLAUDE.md §8): the mesh/material build once in <see cref="_Ready"/>, each hit re-arms via
/// <see cref="Launch"/>, and on expiry it invokes <see cref="Released"/> (the pool reclaims it) instead of
/// freeing. With no callback it frees itself.
///
/// <para>The spark is a soft additive glow on a camera-facing quad, not a solid sphere: a sphere of
/// one flat colour reads as a paper disc hanging over the target, which is what the first spell
/// renders showed. Where a spell has just struck the same spot (the effect layer has already drawn
/// its flare there) the spark is tinted to the school and drawn small, so it marks the hit without
/// covering it.</para>
/// </summary>
public partial class ImpactEffect : Node3D
{
    private const float SeedRadius = 0.12f;
    private const float GrowRadius = 0.55f;
    private const double LifeSeconds = 0.22d;

    /// <summary>How far a spell hit's spark is pulled from its outcome colour toward its school's.</summary>
    private const float SchoolTint = 0.7f;

    /// <summary>A spell hit's spark against a melee blow's: the spell's own flare is the picture.</summary>
    private const float SpellSparkScale = 0.5f;

    private const float SparkAlpha = 0.85f;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private MeshInstance3D _ring = null!;
    private StandardMaterial3D _ringMaterial = null!;
    private Color _color = Colors.White;
    private float _scale = 1f;
    private bool _showRing;
    private double _age;
    private bool _active; // inert until Launch arms it

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the effect frees itself.</summary>
    public Action<ImpactEffect>? Released { get; set; }

    public override void _Ready()
    {
        // White at the centre, nothing at the rim: the quad has no edge.
        var falloff = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f) },
        };
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = falloff,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 64,
                Height = 64,
            },
        };
        _mesh = new MeshInstance3D
        {
            // The glow's bright heart fills about the sphere this used to be.
            Mesh = new QuadMesh { Size = Vector2.One * (SeedRadius * 4f) },
            MaterialOverride = _material,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // Off the layer ground-mark decals project onto (see VfxMaterials.RenderLayer).
            Layers = Magic.Vfx.VfxMaterials.RenderLayer,
        };
        AddChild(_mesh);

        // The shock ring of a parry, a guard break or a poise break: a flat torus that races outward
        // faster than the spark swells, so these three read as events rather than as a bigger puff.
        _ringMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1f, RingSegments = 24 },
            MaterialOverride = _ringMaterial,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = Magic.Vfx.VfxMaterials.RenderLayer,
        };
        AddChild(_ring);
    }

    /// <summary>(Re)arms the spark with a tint, a size multiplier and, optionally, a shock ring. Add it
    /// to the tree and set GlobalPosition first.</summary>
    public void Launch(Color color, float scale = 1f, bool ring = false)
    {
        // A spell hit is marked in its school's colour. The feedback layer hands this spark a colour
        // by outcome only (it is not told a school), so the spark asks the effect layer whether a
        // spell has just struck where it stands. A melee blow finds nothing and keeps its colour.
        Color school = default;
        bool spell = IsInsideTree() && Magic.Vfx.SpellVfx.TryRecentImpactTint(GlobalPosition, out school);
        if (spell)
        {
            color = color.Lerp(school, SchoolTint);
        }

        _color = color;
        _scale = Mathf.Clamp(scale, 0.3f, 3f) * (spell ? SpellSparkScale : 1f);
        _showRing = ring;
        _age = 0d;
        _active = true;
        _mesh.Visible = true;
        _mesh.Scale = Vector3.One;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, SparkAlpha);
        _ring.Visible = ring;
        _ring.Scale = Vector3.One * 0.1f;
        _ringMaterial.Emission = color;
        _ringMaterial.AlbedoColor = new Color(color.R, color.G, color.B, 0.9f);
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }

        _age += delta;
        float t = (float)(_age / LifeSeconds);
        if (t >= 1f)
        {
            _active = false;
            _mesh.Visible = false;
            _ring.Visible = false;
            if (Released != null)
            {
                Released(this);
            }
            else
            {
                QueueFree();
            }

            return;
        }

        _mesh.Scale = Vector3.One * Mathf.Lerp(1f, GrowRadius * _scale / SeedRadius, t);
        _material.AlbedoColor = new Color(_color.R, _color.G, _color.B, SparkAlpha * (1f - t));

        if (_showRing)
        {
            // Eased out, so it snaps open on the frame of the blow and lingers thin at the edge.
            float open = 1f - ((1f - t) * (1f - t));
            _ring.Scale = Vector3.One * Mathf.Lerp(0.1f, 1.5f * _scale, open);
            _ringMaterial.AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.9f * (1f - t));
        }
    }
}
