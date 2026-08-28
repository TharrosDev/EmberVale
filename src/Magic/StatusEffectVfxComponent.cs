using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Player;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// World-space visuals for status effects (Phase 30I): while a status afflicts this entity, a small
/// looping school-tinted particle swirl hangs on the body — burning reads orange, chill ice-blue,
/// regrowth green — so an affliction is legible at a glance without reading the HUD. Purely
/// cosmetic: driven entirely by <see cref="StatusEffectAppliedEvent"/>/<see cref="StatusEffectRemovedEvent"/>;
/// the gameplay (ticks/modifiers) stays in <see cref="StatusEffectsComponent"/>.
/// </summary>
[GlobalClass]
public partial class StatusEffectVfxComponent : EntityComponent
{
    /// <summary>Height above the entity origin the swirl centres on.</summary>
    [Export] public float SwirlHeight { get; set; } = 1.1f;

    private readonly Dictionary<string, GpuParticles3D> _active = new();

    protected override void OnInitialize()
    {
        EventBus.Instance?.Subscribe<StatusEffectAppliedEvent>(OnApplied);
        EventBus.Instance?.Subscribe<StatusEffectRemovedEvent>(OnRemoved);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
        EventBus.Instance?.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);
    }

    private void OnApplied(StatusEffectAppliedEvent e)
    {
        if (!ReferenceEquals(e.Target, Entity) || _active.ContainsKey(e.EffectId) ||
            StatusEffectDatabase.Get(e.EffectId) is not { } effect)
        {
            return;
        }

        // The camera lives inside the local player's first-person body. A billboard orbiting that
        // body inevitably crosses the near plane and becomes a screen-sized translucent square —
        // especially obvious when several effects are active. The HUD already carries the local
        // player's effect chips; world swirls are for actors the player is looking at.
        if (Entity?.Body is PlayerCharacter)
        {
            return;
        }

        GpuParticles3D swirl = BuildSwirl(SpellSchools.Color(effect.School));
        Entity!.Body.AddChild(swirl);
        swirl.Position = new Vector3(0f, SwirlHeight, 0f);
        _active[e.EffectId] = swirl;
    }

    private void OnRemoved(StatusEffectRemovedEvent e)
    {
        if (!ReferenceEquals(e.Target, Entity) || !_active.Remove(e.EffectId, out GpuParticles3D? swirl) ||
            !IsInstanceValid(swirl))
        {
            return;
        }

        // This handler is reached during teardown as well as during play: StatusEffectsComponent's
        // OnTeardown calls ClearAll, which publishes a removal for every live effect. By then the
        // swirl may already have left the tree with the body, and GetTree() returns null there — so
        // the fade timer was a null dereference on the ordinary path of a cell unload or a death
        // despawn. Nothing to fade into once the world is going away; just drop it.
        if (!swirl.IsInsideTree())
        {
            swirl.QueueFree();
            return;
        }

        // Stop emitting and let the last particles fade before freeing.
        swirl.Emitting = false;
        swirl.GetTree().CreateTimer(1.2).Timeout += () =>
        {
            if (IsInstanceValid(swirl))
            {
                swirl.QueueFree();
            }
        };
    }

    /// <summary>A small orbiting-ember swirl tinted to the school colour, built in code so no scene
    /// asset is needed (real particle art is a Phase 53 refinement).</summary>
    private static GpuParticles3D BuildSwirl(Color tint)
    {
        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius = 0.38f,
            EmissionRingInnerRadius = 0.30f,
            EmissionRingHeight = 0.1f,
            EmissionRingAxis = Vector3.Up,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 12f,
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.5f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.5f,
            ScaleMax = 1.0f,
        };

        var mesh = new QuadMesh { Size = new Vector2(0.09f, 0.09f) };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = 1.6f,
        };

        return new GpuParticles3D
        {
            Name = "StatusVfx",
            ProcessMaterial = material,
            DrawPass1 = mesh,
            Amount = 10,
            Lifetime = 1.1,
            Emitting = true,
        };
    }
}
