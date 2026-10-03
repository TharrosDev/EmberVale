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
    private string _resolvedClip = string.Empty;

    protected override string Flag => "--enemy-shots";
    protected override string OutputDir => "user://enemy_shots";

    protected override void BuildShotList()
    {
        string requestedId = OS.GetEnvironment("EMBERVALE_ENEMY_SHOT_ID").Trim();
        if (requestedId.Length > 0 &&
            (!requestedId.StartsWith("enemy.", StringComparison.Ordinal) ||
             EnemyArchetypeDatabase.Get(requestedId) == null))
        {
            GD.PushError($"--enemy-shots: unknown EMBERVALE_ENEMY_SHOT_ID '{requestedId}'");
            return;
        }
        IEnumerable<string> selected = requestedId.Length == 0 ? Priority : new[] { requestedId };
        foreach (string id in selected)
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
        _slot = slot;
        _resolvedClip = string.Empty;
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerCameraRig>() is not { } cameraRig ||
            cameraRig.Camera is not { } camera ||
            EnemyArchetypeDatabase.Get(id) is not { } archetype)
        {
            return;
        }

        // The real gameplay rig owns the camera every frame. Once the harness selects that camera,
        // stop the rig from replacing the requested orbit during ShotHarness's capture hold.
        cameraRig.ProcessMode = ProcessModeEnum.Disabled;
        if (player.GetComponent<CameraOcclusion>() is { } occlusion)
        {
            // The shot camera is examining the subject, not looking back toward the player.
            // Gameplay obstruction fading would otherwise make rear views of the subject translucent.
            occlusion.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (player.GetNodeOrNull<Node3D>("BodyMesh") is { } playerVisual)
        {
            // Keep the real player, camera, collision and session alive, but clear its model from
            // the orbit views. The separate height reference supplies scale without hiding the subject.
            playerVisual.Visible = false;
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
        // AddChild has run the real component initialization and installed the authored/shared
        // libraries. Stop only its selector (and inherited AnimationTree) before choosing the pose.
        if (_subject.GetComponent<CharacterAnimationComponent>() is { } animation)
        {
            animation.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (_subject.GetComponent<EnemyAIComponent>() is { } ai)
        {
            ai.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (_subject.GetComponent<Movement.LocomotionComponent>() is { } locomotion)
        {
            locomotion.ProcessMode = ProcessModeEnum.Disabled;
        }

        CallDeferred(MethodName.PlayRequestedSlot);

        // Imported skinned AABBs include bind-space extremes for several source packs, so gameplay
        // capsule dimensions are the stable framing contract (and the collision scale being tested).
        float height = archetype.CapsuleHeight;
        float width = archetype.CapsuleRadius * 2f;
        float distance = Mathf.Max(4.8f, Mathf.Max(height * 2.10f, archetype.CapsuleRadius * 5.0f));
        float angle = Mathf.DegToRad(angleDegrees);
        Vector3 target = ground + new Vector3(0f, height * 0.52f, 0f);
        // Enemy factories orient the body toward local -Z. A zero angle must therefore face its
        // front, and the requested left/right views must follow the subject's actual world basis.
        Vector3 facing = (_subject.GlobalBasis * Vector3.Forward).Normalized();
        Vector3 orbit = (facing.Rotated(Vector3.Up, -angle) * distance) +
            (Vector3.Up * Mathf.Max(0.25f, height * 0.10f));
        camera.Fov = 42f;
        camera.GlobalPosition = target + orbit;
        camera.LookAt(target, Vector3.Up);

        _scaleReference = PlayerScaleReference();
        GetTree().CurrentScene.AddChild(_scaleReference);
        _scaleReference.GlobalPosition = ground - (camera.GlobalBasis.X * (width * 0.72f + 0.45f));
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
        _resolvedClip = AnimationClips.Resolve(player.GetAnimationList(), _slot);
        if (_resolvedClip.Length > 0)
        {
            player.Play(_resolvedClip);
            player.Seek(_slot == "death" ? 0.55 : _slot is "attack" or "hit" ? 0.32 : 0.15, update: true);
            player.Pause();
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
        if (_resolvedClip.Length == 0)
        {
            return $"no clip resolves for required '{_slot}' state";
        }
        if ((string)animation.AssignedAnimation != _resolvedClip)
        {
            return $"requested '{_slot}' clip '{_resolvedClip}', but '{animation.AssignedAnimation}' owns the pose";
        }
        if (animation.IsPlaying())
        {
            return $"requested '{_slot}' pose is advancing instead of paused at the capture sample";
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
