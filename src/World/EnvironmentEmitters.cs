using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>World-owned budget for authored practical lights and environmental particles.</summary>
public sealed class EnvironmentEmitters
{
    private sealed class Lamp
    {
        public required OmniLight3D Node;
        public required float Energy;
        public required bool Shadows;
        // Magical landmarks keep their authored intensity. Lamps/fires become evening anchors.
        public required bool Practical;
        public float DistanceSquared;
        public float SentEnergy = float.NaN;
        public bool SentShadows;
        public bool Fresh = true;
    }

    private readonly List<Lamp> _lights = new();
    private readonly List<(GpuParticles3D Node, ParticleProcessMaterial Material, Vector3 Gravity)> _particles = new();
    private float _lightDistance = float.NaN, _particleScale = float.NaN;
    private Vector3 _wind = new(float.NaN, 0f, 0f);

    public void Observe(Node node)
    {
        // Runtime combat effects retain their own lifetimes and intensity. Only authored world nodes.
        if (node.Owner == null) return;
        if (node is OmniLight3D light)
        {
            _lights.Add(new Lamp
            {
                Node = light,
                Energy = light.LightEnergy,
                Shadows = light.ShadowEnabled,
                Practical = light.Name.ToString().Contains("Light"),
                SentShadows = light.ShadowEnabled,
            });
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
            // A system that streams in after the last update still gets the current wind and scale.
            _particleScale = float.NaN;
        }
    }

    /// <summary>
    /// Runs four times a second. Everything it sends to the engine is sent on change: the fade range
    /// when the preset moves, a lamp's energy when the daylight does, its shadow flag when its rank
    /// does, and the particles' wind and amount when either differs from what they last received.
    /// </summary>
    public void Update(Vector3 camera, Vector3 wind, float day, float particleScale, int shadowLights, float lightDistance)
    {
        bool fadeChanged = lightDistance != _lightDistance;
        _lightDistance = lightDistance;
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(_lights[i].Node)) _lights.RemoveAt(i);
        }

        // The nearest-first order exists only to hand out the shadow budget. With no budget (every
        // tier below High) there is nothing to rank, so no positions are read and nothing is sorted.
        if (shadowLights > 0)
        {
            foreach (Lamp light in _lights)
                light.DistanceSquared = light.Node.GlobalPosition.DistanceSquaredTo(camera);
            _lights.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        }

        int shadowCount = 0;
        foreach (Lamp light in _lights)
        {
            if (fadeChanged || light.Fresh)
            {
                light.Node.DistanceFadeBegin = lightDistance * .7f;
                light.Node.DistanceFadeLength = lightDistance * .3f;
                light.Fresh = false;
            }
            bool shadows = shadowLights > 0 && light.Shadows && light.DistanceSquared < 400f && shadowCount++ < shadowLights;
            if (shadows != light.SentShadows)
            {
                light.Node.ShadowEnabled = shadows;
                light.SentShadows = shadows;
            }
            float energy = light.Energy * (light.Practical ? Mathf.Lerp(1f, .4f, day) : 1f);
            if (energy != light.SentEnergy)
            {
                light.Node.LightEnergy = energy;
                light.SentEnergy = energy;
            }
        }

        bool particlesChanged = wind != _wind || particleScale != _particleScale;
        _wind = wind;
        _particleScale = particleScale;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var item = _particles[i];
            if (!GodotObject.IsInstanceValid(item.Node)) { _particles.RemoveAt(i); continue; }
            if (!particlesChanged) continue;
            item.Material.Gravity = item.Gravity + wind * .08f;
            item.Node.AmountRatio = particleScale;
        }
    }

    public void Clear() { _lights.Clear(); _particles.Clear(); }
}
