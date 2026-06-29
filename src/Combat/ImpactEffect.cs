using System;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A short-lived expand-and-fade spark marking a combat impact (Phase 29C). Purely cosmetic — the damage
/// is already resolved. The melee/feedback analogue of <see cref="Magic.SpellFlash"/>, but <b>poolable</b>
/// (CLAUDE.md §8): the mesh/material build once in <see cref="_Ready"/>, each hit re-arms via
/// <see cref="Launch"/>, and on expiry it invokes <see cref="Released"/> (the pool reclaims it) instead of
/// freeing. With no callback it frees itself.
/// </summary>
public partial class ImpactEffect : Node3D
{
    private const float SeedRadius = 0.12f;
    private const float GrowRadius = 0.55f;
    private const double LifeSeconds = 0.22d;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Color _color = Colors.White;
    private double _age;
    private bool _active; // inert until Launch arms it

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the effect frees itself.</summary>
    public Action<ImpactEffect>? Released { get; set; }

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
        };
        _mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = SeedRadius, Height = SeedRadius * 2f },
            MaterialOverride = _material,
            Visible = false,
        };
        AddChild(_mesh);
    }

    /// <summary>(Re)arms the spark with a tint. Add it to the tree and set GlobalPosition first.</summary>
    public void Launch(Color color)
    {
        _color = color;
        _age = 0d;
        _active = true;
        _mesh.Visible = true;
        _mesh.Scale = Vector3.One;
        _material.Emission = color;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, 0.7f);
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

        _mesh.Scale = Vector3.One * Mathf.Lerp(1f, GrowRadius / SeedRadius, t);
        _material.AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.7f * (1f - t));
    }
}
