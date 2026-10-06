using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>What one <see cref="VfxDistortion"/> is asked to bend.</summary>
internal struct VfxDistortionSpec
{
    public Vector3 Position;

    /// <summary>Metres the wave reaches.</summary>
    public float Radius;

    /// <summary>Seconds a one-shot wave takes to cross <see cref="Radius"/>.</summary>
    public float Life;

    /// <summary>How far the image is pushed, in screen UV (0.02 to 0.05 is a blast).</summary>
    public float Strength;

    /// <summary>The wave closes on the centre instead of leaving it.</summary>
    public bool Inward;

    /// <summary>Holds and breathes until stopped (a gravity well) instead of passing once.</summary>
    public bool Sustain;
}

/// <summary>
/// A pressure wave: a sphere that refracts what is behind it (<c>vfx_distort.gdshader</c>). Only
/// ever spawned where <see cref="VfxRecipeRules.Plan"/> allows it: the High and Ultra tiers, and
/// never under Reduced Motion, because moving the whole image is exactly what that setting is for.
/// </summary>
public partial class VfxDistortion : VfxEffect
{
    private readonly MeshInstance3D _sphere;
    private readonly ShaderMaterial _material;
    private VfxDistortionSpec _spec;

    public VfxDistortion()
    {
        _material = VfxMaterials.Distort();
        _sphere = new MeshInstance3D
        {
            Name = "Shell",
            Mesh = VfxMaterials.Sphere,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        AddChild(_sphere);
    }

    /// <summary>Styles the wave for a new life. Call after <see cref="VfxEffect.Begin"/>.</summary>
    internal void Arm(in VfxDistortionSpec spec)
    {
        _spec = spec;
        GlobalPosition = spec.Position;
        _material.SetShaderParameter(VfxMaterials.Strength, spec.Strength > 0f ? spec.Strength : 0.035f);
        Apply(spec.Sustain ? 1f : 0f, spec.Sustain ? 0f : 1f);
    }

    protected override bool Tick(float delta)
    {
        if (_spec.Sustain)
        {
            if (Stopping && StopAge >= StopFadeSeconds)
            {
                return false;
            }

            float breathe = 0.92f + (0.08f * Mathf.Sin((float)Age * 2.4f));
            Apply(breathe, Mathf.Clamp((float)Age / 0.3f, 0f, 1f) * StopFade() * 0.7f);
            return true;
        }

        float t = (float)(Age / Mathf.Max(0.05f, _spec.Life));
        if (t >= 1f)
        {
            return false;
        }

        float along = _spec.Inward ? 1f - t : t;
        float eased = 1f - ((1f - along) * (1f - along));
        Apply(Mathf.Lerp(0.15f, 1f, eased), (1f - t) * Mathf.Sqrt(1f - t));
        return true;
    }

    private void Apply(float size, float opacity)
    {
        _sphere.Scale = Vector3.One * (Mathf.Max(0.05f, _spec.Radius) * 2f * size);
        _material.SetShaderParameter(VfxMaterials.Opacity, opacity);
    }
}
