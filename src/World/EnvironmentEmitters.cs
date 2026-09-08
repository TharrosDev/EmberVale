using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>World-owned budget for authored practical lights and environmental particles.</summary>
public sealed class EnvironmentEmitters
{
    private sealed record Lamp(OmniLight3D Node, float Energy, bool Shadows);
    private readonly List<Lamp> _lights = new();
    private readonly List<(GpuParticles3D Node, ParticleProcessMaterial Material, Vector3 Gravity)> _particles = new();

    public void Observe(Node node)
    {
        // Runtime combat effects retain their own lifetimes and intensity. Only authored world nodes.
        if (node.Owner == null) return;
        if (node is OmniLight3D light)
        {
            _lights.Add(new Lamp(light, light.LightEnergy, light.ShadowEnabled));
            light.DistanceFadeEnabled = true;
            light.DistanceFadeBegin = 35f;
            light.DistanceFadeLength = 15f;
        }
        if (node is GpuParticles3D particles && particles.ProcessMaterial is ParticleProcessMaterial process)
        {
            var local = (ParticleProcessMaterial)process.Duplicate();
            particles.ProcessMaterial = local;
            _particles.Add((particles, local, local.Gravity));
            particles.VisibilityRangeEnd = 60f;
            particles.VisibilityRangeEndMargin = 10f;
            particles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    public void Update(Vector3 camera, Vector3 wind, float day, RenderQualityResource quality)
    {
        _lights.RemoveAll(x => !GodotObject.IsInstanceValid(x.Node));
        _lights.Sort((a, b) => a.Node.GlobalPosition.DistanceSquaredTo(camera).CompareTo(b.Node.GlobalPosition.DistanceSquaredTo(camera)));
        int shadowCount = 0;
        foreach (Lamp light in _lights)
        {
            float distance = light.Node.GlobalPosition.DistanceTo(camera);
            light.Node.DistanceFadeBegin = quality.LocalLightDistance * .7f;
            light.Node.DistanceFadeLength = quality.LocalLightDistance * .3f;
            light.Node.ShadowEnabled = light.Shadows && distance < 20f && shadowCount++ < quality.LocalShadowLights;
            // Magical landmarks keep their authored intensity. Lamps/fires become evening anchors.
            bool practical = light.Node.Name.ToString().Contains("Light");
            light.Node.LightEnergy = light.Energy * (practical ? Mathf.Lerp(1f, .4f, day) : 1f);
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var item = _particles[i];
            if (!GodotObject.IsInstanceValid(item.Node)) { _particles.RemoveAt(i); continue; }
            item.Material.Gravity = item.Gravity + wind * .08f;
            item.Node.AmountRatio = quality.ParticleScale;
        }
    }

    public void Clear() { _lights.Clear(); _particles.Clear(); }
}
