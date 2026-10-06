using System.Collections.Generic;
using Embervale.Races;
using Godot;

namespace Embervale.Appearance;

/// <summary>
/// Applies a character's chosen look to the player body model. Shared by <c>PlayerFactory.Create</c> and the
/// creator's live preview, so the preview is the same code path as the world. Material-only: the model's one
/// surface gets the <c>player_body.gdshader</c> region tint, and the Build slot scales the body's X/Z.
/// </summary>
public static class PlayerAppearance
{
    public const string ShaderPath = "res://assets/shaders/player_body.gdshader";
    public const string MaskPath = "res://assets/models/characters/chr_player_base_mask.png";

    /// <summary>One resolved option per <see cref="AppearanceSlot"/> for a profile: its ids that the race offers,
    /// otherwise each slot's default. A slot with no authored default is null.</summary>
    public static AppearanceOptionResource?[] Resolve(CharacterProfile profile) =>
        Resolve(RaceDatabase.Get(profile.RaceId), profile.AppearanceOptionIds);

    public static AppearanceOptionResource?[] Resolve(RaceResource? race, IEnumerable<string>? ids)
    {
        string[] picks = AppearanceRules.Resolve(
            ids,
            id => AppearanceDatabase.Get(id)?.Slot,
            id => race != null && race.AppearanceOptionIds.Contains(id),
            slot => AppearanceDatabase.DefaultFor(slot)?.Id);

        var options = new AppearanceOptionResource?[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            options[i] = AppearanceDatabase.Get(picks[i]);
        }

        return options;
    }

    /// <summary>The options the creator offers for <paramref name="slot"/>: the default first, then the
    /// race's allowed ones in authored order.</summary>
    public static List<AppearanceOptionResource> OptionsFor(RaceResource? race, AppearanceSlot slot)
    {
        var list = new List<AppearanceOptionResource>();
        if (AppearanceDatabase.DefaultFor(slot) is { } fallback)
        {
            list.Add(fallback);
        }

        foreach (AppearanceOptionResource option in AppearanceDatabase.All)
        {
            if (option.Slot == slot && !option.IsDefault && race != null && race.AppearanceOptionIds.Contains(option.Id))
            {
                list.Add(option);
            }
        }

        return list;
    }

    /// <summary>Tints <paramref name="body"/> (the glb instance) and scales its width. Safe to call again on a
    /// live body: an already-converted surface is updated in place, which keeps any copy the corruption
    /// controller took.</summary>
    public static void Apply(Node3D body, IReadOnlyList<AppearanceOptionResource?> picks)
    {
        AppearanceOptionResource? build = Pick(picks, AppearanceSlot.Build);
        float width = build != null ? build.BuildScale : 1f;
        body.Scale = new Vector3(width, 1f, width);

        ApplyMaterials(body, picks);
    }

    /// <summary>The body shader material under <paramref name="node"/> (the one the corruption controller also
    /// drives), or null when the model is the stand-in capsule.</summary>
    public static ShaderMaterial? FindMaterial(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if (instance.GetActiveMaterial(i) is ShaderMaterial { Shader.ResourcePath: ShaderPath } material)
                {
                    return material;
                }
            }
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindMaterial(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static AppearanceOptionResource? Pick(IReadOnlyList<AppearanceOptionResource?> picks, AppearanceSlot slot) =>
        (int)slot < picks.Count ? picks[(int)slot] : null;

    private static void ApplyMaterials(Node node, IReadOnlyList<AppearanceOptionResource?> picks)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                ShaderMaterial? material = instance.GetActiveMaterial(i) switch
                {
                    ShaderMaterial { Shader.ResourcePath: ShaderPath } existing => existing,
                    StandardMaterial3D { AlbedoTexture: { } } source => ConvertFrom(source),
                    _ => null,
                };

                if (material == null)
                {
                    continue;
                }

                instance.SetSurfaceOverrideMaterial(i, material);
                SetTints(material, picks);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyMaterials(child, picks);
        }
    }

    /// <summary>The body shader carrying the imported glTF material's texture and lighting values, so the
    /// unmodified look is unchanged.</summary>
    private static ShaderMaterial? ConvertFrom(StandardMaterial3D source)
    {
        if (GD.Load<Shader>(ShaderPath) is not { } shader)
        {
            return null;
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo_tex", source.AlbedoTexture);
        material.SetShaderParameter("region_mask", GD.Load<Texture2D>(MaskPath));
        material.SetShaderParameter("metallic_amount", source.Metallic);
        material.SetShaderParameter("roughness_amount", source.Roughness);
        material.SetShaderParameter("specular_amount", source.MetallicSpecular);
        material.SetShaderParameter("skin_ref", ToColor(AppearanceRules.SkinReference));
        material.SetShaderParameter("hair_ref", ToColor(AppearanceRules.HairReference));
        material.SetShaderParameter("eye_ref", ToColor(AppearanceRules.EyeReference));
        return material;
    }

    private static void SetTints(ShaderMaterial material, IReadOnlyList<AppearanceOptionResource?> picks)
    {
        material.SetShaderParameter("skin_tint", TintOr(picks, AppearanceSlot.Skin, AppearanceRules.SkinReference));
        material.SetShaderParameter("hair_tint", TintOr(picks, AppearanceSlot.Hair, AppearanceRules.HairReference));
        material.SetShaderParameter("eye_tint", TintOr(picks, AppearanceSlot.Eyes, AppearanceRules.EyeReference));
        material.SetShaderParameter("ember_tint", TintOr(picks, AppearanceSlot.Ember, AppearanceRules.EmberReference));
    }

    private static Color TintOr(IReadOnlyList<AppearanceOptionResource?> picks, AppearanceSlot slot, (float R, float G, float B) reference) =>
        Pick(picks, slot)?.Tint ?? ToColor(reference);

    private static Color ToColor((float R, float G, float B) c) => new(c.R, c.G, c.B);
}
