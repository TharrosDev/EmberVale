using System;
using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Player;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Production enemy visual QA. It builds real archetypes through <see cref="EnemyArchetypeFactory"/>,
/// therefore exercising authored model paths, deterministic identity attachments, gameplay scale,
/// collision roots and the exact animation resolver used in encounters.
/// </summary>
public sealed partial class EnemyShots : ShotHarness
{
    private static readonly string[] Priority =
    {
        "enemy.thornback_boar", "enemy.barrow_wight", "enemy.grave_shade", "enemy.clan_shaman",
        "enemy.hollow_necromancer", "enemy.soldier", "enemy.bandit", "enemy.syndicate_enforcer",
        "enemy.wolf", "enemy.dire_wolf", "enemy.frost_stalker", "enemy.cinder_wisp",
        "enemy.storm_mote", "enemy.rime_shard", "enemy.ash_maw", "enemy.ruin_crawler",
        "enemy.ward_golem", "enemy.stone_sentinel", "enemy.wild_dragon", "enemy.ash_dragon",
        "enemy.frost_drake", "enemy.ancient_dragon", "enemy.iron_king",
    };

    private static readonly (string Suffix, float Angle, string Slot)[] Views =
    {
        ("front", 0f, "idle"), ("front-3q", -35f, "idle"), ("left", -90f, "idle"),
        ("rear", 180f, "idle"), ("rear-3q", 145f, "idle"), ("right", 90f, "idle"),
        ("locomotion", -35f, "run"), ("attack", -35f, "attack"),
        ("hit", -35f, "hit"), ("death", -35f, "death"),
    };

    private EnemyEntity? _subject;
    private Node3D? _scaleReference;
    private string _slot = "idle";

    protected override string Flag => "--enemy-shots";
    protected override string OutputDir => "user://enemy_shots";

    protected override void BuildShotList()
    {
        foreach (string id in Priority)
        {
            string stem = id["enemy.".Length..].Replace('_', '-');
            foreach ((string suffix, float angle, string slot) in Views)
            {
                string capturedId = id;
                float capturedAngle = angle;
                string capturedSlot = slot;
                Shot($"{stem}--{suffix}", () => Frame(capturedId, capturedAngle, capturedSlot));
            }
        }
    }

    private void Frame(string id, float angleDegrees, string slot)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerCameraRig>() is not { Camera: { } camera } ||
            EnemyArchetypeDatabase.Get(id) is not { } archetype)
        {
            return;
        }

        // Freeze the player: the router is the one component that reads input every frame.
        if (player.GetComponent<PlayerInputRouter>() is { } router)
        {
            router.ProcessMode = ProcessModeEnum.Disabled;
        }

        if (_subject != null && IsInstanceValid(_subject))
        {
            _subject.QueueFree();
        }
        if (_scaleReference != null && IsInstanceValid(_scaleReference))
        {
            _scaleReference.QueueFree();
        }

        Vector3 ground = player.GlobalPosition + new Vector3(0f, 0.04f, -4.0f);
        _subject = EnemyArchetypeFactory.Create(archetype, ground);
        GetTree().CurrentScene.AddChild(_subject);
        if (_subject.GetComponent<EnemyAIComponent>() is { } ai)
        {
            ai.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (_subject.GetComponent<Movement.LocomotionComponent>() is { } locomotion)
        {
            locomotion.ProcessMode = ProcessModeEnum.Disabled;
        }

        _slot = slot;
        CallDeferred(MethodName.PlayRequestedSlot);

        // Imported skinned AABBs include bind-space extremes for several source packs, so gameplay
        // capsule dimensions are the stable framing contract (and the collision scale being tested).
        float height = archetype.CapsuleHeight;
        float width = archetype.CapsuleRadius * 2f;
        float distance = Mathf.Max(4.8f, Mathf.Max(height * 2.10f, archetype.CapsuleRadius * 5.0f));
        float angle = Mathf.DegToRad(angleDegrees);
        Vector3 target = ground + new Vector3(0f, height * 0.52f, 0f);
        Vector3 orbit = new(Mathf.Sin(angle) * distance, Mathf.Max(0.25f, height * 0.10f),
            Mathf.Cos(angle) * distance);
        camera.Fov = 42f;
        camera.GlobalPosition = target + orbit;
        camera.LookAt(target, Vector3.Up);

        _scaleReference = PlayerScaleReference();
        GetTree().CurrentScene.AddChild(_scaleReference);
        _scaleReference.GlobalPosition = ground + new Vector3(-width * 0.72f - 0.45f, 0f, 0f);
    }

    private static Node3D PlayerScaleReference()
    {
        var root = new Node3D { Name = "PlayerHeightReference" };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("b68a3d"), Roughness = 0.88f,
        };
        var body = new MeshInstance3D
        {
            Mesh = new CylinderMesh { Height = 1.42f, TopRadius = 0.20f, BottomRadius = 0.24f },
            MaterialOverride = material,
            Position = new Vector3(0f, 0.71f, 0f),
        };
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
            MaterialOverride = material,
            Position = new Vector3(0f, 1.64f, 0f),
        };
        root.AddChild(body);
        root.AddChild(head);
        return root;
    }

    private void PlayRequestedSlot()
    {
        if (_subject == null || !IsInstanceValid(_subject) || FindAnimationPlayer(_subject) is not { } player)
        {
            return;
        }
        string clip = AnimationClips.Resolve(player.GetAnimationList(), _slot);
        if (clip.Length > 0)
        {
            player.Play(clip);
            player.Seek(_slot == "death" ? 0.55 : _slot is "attack" or "hit" ? 0.32 : 0.15, update: true);
        }
    }

    protected override string? ValidateShotState(string name)
    {
        if (_subject == null || !IsInstanceValid(_subject))
        {
            return "the real enemy factory did not produce a subject";
        }
        if (_subject.GetNodeOrNull<Node3D>("Mesh") is null)
        {
            return "production model root is missing";
        }
        if (_subject.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not CapsuleShape3D capsule)
        {
            return "gameplay capsule is missing";
        }
        if (capsule.Height <= 0f || capsule.Radius <= 0f)
        {
            return $"invalid gameplay capsule {capsule.Radius} × {capsule.Height}";
        }
        if (FindAnimationPlayer(_subject) is not { } animation)
        {
            return "production AnimationPlayer is missing";
        }
        if (AnimationClips.Resolve(animation.GetAnimationList(), _slot).Length == 0)
        {
            return $"no clip resolves for required '{_slot}' state";
        }
        if (EnemyVisualKit.Resolve(_subject.TemplateId) is { } profile)
        {
            Skeleton3D? skeleton = FindSkeleton(_subject);
            if (skeleton == null)
            {
                return $"identity profile '{profile.Id}' has no skeleton";
            }
            foreach (EnemyVisualKit.Piece piece in profile.Pieces)
            {
                if (skeleton.FindChild($"Identity_{piece.Name}", recursive: true, owned: false) == null)
                {
                    return $"identity piece '{piece.Name}' did not attach";
                }
            }
        }
        return null;
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player)
        {
            return player;
        }
        foreach (Node child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
        {
            return skeleton;
        }
        foreach (Node child in node.GetChildren())
        {
            if (FindSkeleton(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
