using System;
using System.Collections.Generic;
using Embervale.Combat;
using Godot;

namespace Embervale.World;

/// <summary>Canonical layer/mask audit for streamed content.</summary>
public static class WorldPhysicsContract
{
    public static IReadOnlyList<string> Validate(Node root)
    {
        var issues = new List<string>();
        Visit(root, issues);
        return issues;
    }

    public static IReadOnlyList<string> Validate(
        string path, string type, uint layer, uint mask)
    {
        var issues = new List<string>();
        bool area = type == nameof(Area3D);
        string name = path.ToLowerInvariant();
        if (area && CombatLayers.IsSensorLayer(layer) && (layer & CombatLayers.PhysicalWorld) != 0u)
        {
            issues.Add($"{path}: sensor Area3D also occupies a physical world layer");
        }
        if ((name.Contains("hurtbox") || name.Contains("hitbox")) &&
            (layer & CombatLayers.PhysicalWorld) != 0u)
        {
            issues.Add($"{path}: combat volume physically blocks the world");
        }
        if ((name.Contains("interaction") || name.Contains("trigger")) && !area)
        {
            issues.Add($"{path}: interaction/trigger must be an Area3D");
        }
        if (name.Contains("terraincollider") && (layer & CombatLayers.WorldStatic) == 0u)
        {
            issues.Add($"{path}: terrain is missing WorldStatic");
        }
        if (name.Contains("camerablocker") && (layer & CombatLayers.CameraBlocker) == 0u)
        {
            issues.Add($"{path}: named camera blocker is missing CameraBlocker");
        }
        if ((layer & CombatLayers.Hurtbox) != 0u && (mask & CombatLayers.PhysicalWorld) != 0u)
        {
            issues.Add($"{path}: Hurtbox observes physical world collision");
        }
        if ((layer & CombatLayers.Hitbox) != 0u && (mask & CombatLayers.Hurtbox) == 0u)
        {
            issues.Add($"{path}: Hitbox does not observe Hurtbox");
        }
        return issues;
    }

    private static void Visit(Node node, List<string> issues)
    {
        if (node is CollisionObject3D collision)
        {
            issues.AddRange(Validate(
                collision.GetPath().ToString(), collision.GetType().Name,
                collision.CollisionLayer, collision.CollisionMask));
        }
        foreach (Node child in node.GetChildren())
        {
            Visit(child, issues);
        }
    }
}
