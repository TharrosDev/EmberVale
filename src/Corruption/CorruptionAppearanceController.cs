using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Corruption;

/// <summary>
/// Drives the player's outward appearance from their corruption tier — the <b>placeholder</b>
/// stand-in (ash-grey skin + ember emissive "eye-glow / ash-vein") for the real materials and
/// VFX that Phase 30 (Animation, Models &amp; Visual Identity) will author. It tints the player's
/// body mesh per <see cref="CorruptionTier"/> off <see cref="CorruptionTierChangedEvent"/>.
///
/// <para><b>Phase 30 hook:</b> keep this component and its event wiring; replace the placeholder
/// <see cref="StandardMaterial3D"/> tinting in <see cref="Apply"/> (and the stand-in body mesh the
/// factory adds) with the shipped model's materials / shader params / VFX emitters. The contract
/// other systems rely on is just "appearance follows tier" — nothing reads how it's drawn.</para>
/// </summary>
[GlobalClass]
public partial class CorruptionAppearanceController : EntityComponent
{
    /// <summary>Node name of the player body mesh this tints (added by <c>PlayerFactory</c>).</summary>
    [Export] public string BodyMeshPath { get; set; } = "BodyMesh";

    private StandardMaterial3D? _material;

    protected override void OnInitialize()
    {
        if (Entity is { } owner && owner.Body.GetNodeOrNull<MeshInstance3D>(BodyMeshPath) is { } mesh)
        {
            // Own a unique material so tinting never bleeds into a shared resource.
            _material = mesh.MaterialOverride as StandardMaterial3D ?? new StandardMaterial3D();
            mesh.MaterialOverride = _material;
        }

        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnTierChanged);
        Apply(CorruptionTier.Untainted);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnTierChanged);
    }

    private void OnTierChanged(CorruptionTierChangedEvent e) => Apply(e.Current);

    /// <summary>Placeholder per-tier look: skin fades toward ash, the ember glow rises. Phase 30
    /// replaces these stand-ins with authored materials/VFX (see the class hook note).</summary>
    private void Apply(CorruptionTier tier)
    {
        if (_material == null)
        {
            return;
        }

        float t = (int)tier / 4f; // 0 Untainted … 4 Embers

        // Healthy skin fades toward ash-grey as corruption deepens.
        Color baseSkin = new(0.62f, 0.60f, 0.58f);
        Color ash = new(0.20f, 0.18f, 0.22f);
        _material.AlbedoColor = baseSkin.Lerp(ash, t);

        // Ember/ash-violet emissive stand-in for eye-glow / ash veins; off while Untainted.
        _material.EmissionEnabled = tier != CorruptionTier.Untainted;
        _material.Emission = new Color(0.58f, 0.20f, 0.30f);
        _material.EmissionEnergyMultiplier = t * 2.0f;
    }
}
