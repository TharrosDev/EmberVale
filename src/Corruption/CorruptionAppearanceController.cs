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
    /// <summary>Node name of the player body visual this tints (added by <c>PlayerFactory</c>) —
    /// either a single stand-in <see cref="MeshInstance3D"/> or the 30B model scene root, whose
    /// mesh surfaces are all tinted.</summary>
    [Export] public string BodyMeshPath { get; set; } = "BodyMesh";

    // Every surface material on the body (uniquely owned so tinting never bleeds into a shared
    // resource) paired with its authored base albedo, so ashing lerps FROM the real colour, and
    // whether it is a skin material (only skin gets the ember-vein emissive — glowing the whole
    // outfit red reads as a rendering bug, not corruption).
    private readonly System.Collections.Generic.List<(StandardMaterial3D Material, Color BaseAlbedo, bool IsSkin)> _surfaces = new();

    // The player body shader (P8) carries its own skin/hair/eye mask, so it takes the tier as shader
    // parameters instead of per-surface colours.
    private readonly System.Collections.Generic.List<ShaderMaterial> _bodyShaders = new();

    protected override void OnInitialize()
    {
        if (Entity is { } owner && owner.Body.GetNodeOrNull<Node3D>(BodyMeshPath) is { } bodyRoot)
        {
            CollectSurfaces(bodyRoot);
        }

        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnTierChanged);
        Apply(CorruptionTier.Untainted);
    }

    /// <summary>Claims a uniquely-owned copy of every mesh surface material under
    /// <paramref name="node"/> (the 30B model has one per palette colour; the stand-in capsule
    /// has one override).</summary>
    private void CollectSurfaces(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is { } res)
        {
            for (int i = 0; i < res.GetSurfaceCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is ShaderMaterial { Shader.ResourcePath: Appearance.PlayerAppearance.ShaderPath } body)
                {
                    var ownedBody = (ShaderMaterial)body.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(i, ownedBody);
                    _bodyShaders.Add(ownedBody);
                    continue;
                }

                StandardMaterial3D owned = mesh.GetActiveMaterial(i) is StandardMaterial3D m
                    ? (StandardMaterial3D)m.Duplicate()
                    : new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f) };
                // A model whose glTF materials are named ("chr_skin", …) gets skin treatment on name;
                // the stand-in capsule has no name and counts as skin so it still shows the tier.
                // (The player body's single "Material_1" never matched, which is why it now has the
                // masked shader above.)
                bool isSkin = owned.ResourceName is not { Length: > 0 } n || n.Contains("skin");
                mesh.SetSurfaceOverrideMaterial(i, owned);
                _surfaces.Add((owned, owned.AlbedoColor, isSkin));
            }
        }

        foreach (Node child in node.GetChildren())
        {
            CollectSurfaces(child);
        }
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnTierChanged);
    }

    private void OnTierChanged(CorruptionTierChangedEvent e) => Apply(e.Current);

    /// <summary>The ART_STYLE §2.2 corruption arc, per tier (30I): how far the body ashes, an extra
    /// skin tint (violet veining early, char late), and the skin emissive (violet → ember).</summary>
    private static (float AshT, Color SkinTint, float TintT, Color Emission, float Energy, float EmberMix) LookFor(CorruptionTier tier) => tier switch
    {
        // EmberMix: how far the glow takes the character's chosen ember colour (violet veining stays violet).
        CorruptionTier.Touched => (0.06f, new Color(0.48f, 0.30f, 0.55f), 0.14f, new Color(0.38f, 0.22f, 0.48f), 0.10f, 0f),
        CorruptionTier.Marked => (0.24f, new Color(0.42f, 0.38f, 0.40f), 0.28f, new Color(0.55f, 0.20f, 0.10f), 0.18f, 0.5f),
        CorruptionTier.Ashbound => (0.45f, new Color(0.20f, 0.16f, 0.15f), 0.40f, new Color(0.72f, 0.28f, 0.06f), 0.40f, 0.85f),
        CorruptionTier.Embers => (0.62f, new Color(0.12f, 0.09f, 0.09f), 0.55f, new Color(0.82f, 0.34f, 0.10f), 0.70f, 1f),
        _ => (0f, Colors.White, 0f, Colors.Black, 0f, 0f),
    };

    /// <summary>Placeholder per-tier look: skin fades toward ash, the ember glow rises. Phase 30
    /// replaces these stand-ins with authored materials/VFX (see the class hook note).</summary>
    private void Apply(CorruptionTier tier)
    {
        (float ashT, Color skinTint, float tintT, Color emission, float energy, float emberMix) = LookFor(tier);
        Color ash = new(0.20f, 0.17f, 0.17f);

        foreach (ShaderMaterial body in _bodyShaders)
        {
            body.SetShaderParameter("ash_amount", ashT);
            body.SetShaderParameter("ash_color", ash);
            body.SetShaderParameter("skin_wash_amount", tintT);
            body.SetShaderParameter("skin_wash_color", skinTint);
            body.SetShaderParameter("glow_color", emission);
            body.SetShaderParameter("glow_energy", energy);
            body.SetShaderParameter("ember_mix", emberMix);
        }

        foreach ((StandardMaterial3D material, Color baseAlbedo, bool isSkin) in _surfaces)
        {
            // Clothing/gear only gathers ash; skin additionally takes the tier's tint
            // (violet veining while Touched, ash-grey patches Marked, char toward Embers).
            Color albedo = baseAlbedo.Lerp(ash, ashT);
            if (isSkin)
            {
                albedo = albedo.Lerp(skinTint, tintT);
            }

            material.AlbedoColor = albedo;

            // The emissive rises through the arc on skin only: faint violet at Touched,
            // turning ember and brightening toward Embers ("skin like banked coals").
            material.EmissionEnabled = isSkin && energy > 0f;
            material.Emission = emission;
            material.EmissionEnergyMultiplier = isSkin ? energy : 0f;
        }
    }
}
