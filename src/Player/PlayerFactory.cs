using Embervale.Entities;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Builds a fully-assembled first-person player actor in code. Constructing it
/// here (rather than a hand-authored <c>.tscn</c>) keeps the node graph, its
/// collision shape and its components in one reviewable place while the project
/// is young; it can be promoted to a packed scene later without changing callers.
/// </summary>
public static class PlayerFactory
{
    private const string PlayerAttributesPath = "res://data/attributes/PlayerAttributes.tres";
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;
    private const float EyeHeight = 1.62f;

    public static CharacterEntity Create(Vector3 position)
    {
        var player = new CharacterEntity
        {
            Name = "Player",
            DisplayName = "Player",
            TemplateId = "player",
            Position = position,
        };

        var collision = new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        };
        player.AddChild(collision);

        AttributeSet attributes = GD.Load<AttributeSet>(PlayerAttributesPath) ?? AttributeSet.CreateDefault();
        player.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes });
        player.AddChild(new LocomotionComponent { Name = "Locomotion" });

        var cameraPivot = new Node3D
        {
            Name = "CameraPivot",
            Position = new Vector3(0f, EyeHeight, 0f),
        };
        player.AddChild(cameraPivot);

        var camera = new Camera3D { Name = "Camera", Current = true };
        cameraPivot.AddChild(camera);

        player.AddChild(new PlayerController
        {
            Name = "Controller",
            CameraPivot = cameraPivot,
            Camera = camera,
        });

        return player;
    }
}
