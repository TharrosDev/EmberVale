using Embervale.Combat;
using Embervale.Core;
using Embervale.Corruption;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Builds a fully-assembled third-person player actor in code. Constructing it
/// here (rather than a hand-authored <c>.tscn</c>) keeps the node graph, its
/// collision shape and its components in one reviewable place while the project
/// is young; it can be promoted to a packed scene later without changing callers.
/// </summary>
public static class PlayerFactory
{
    internal const string PlayerAttributesPath = "res://data/attributes/PlayerAttributes.tres";
    internal const string StartingWeaponPath = "res://data/weapons/IronSword.tres";
    internal const string ProgressionPath = "res://data/progression/PlayerProgression.tres";
    internal const string PlayerModelPath = "res://assets/models/characters/chr_player_base.glb";
    internal const string WeaponModelPath = "res://assets/models/weapons/wpn_sword_iron.glb";
    private const int PlayerTeam = 0;
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;

    // First-person camera (maintainer direction, 2026-07-02): the pitch pivot sits at eye
    // height and the camera rides it directly. The third-person offsets are kept for the
    // Phase 43 cutscene seam — PlayerController.SetFirstPerson(false) swings the camera back
    // out and re-shows the body, so cutscenes can frame the retained third-person rig.
    private const float EyeHeight = 1.62f;
    internal const float ThirdPersonBackDistance = 3.8f;
    internal const float ThirdPersonRise = 0.4f;

    public static PlayerCharacter Create(Vector3 position) =>
        Create(position, Races.CharacterProfile.Human, applyStartingGrants: true);

    /// <summary>Builds the player and applies the chosen creation <paramref name="profile"/>'s race
    /// (Phase 26C). <paramref name="applyStartingGrants"/> is true on New Game (grant the race's innate
    /// perks/spells/reputation) and false on load (the saved overlay restores them).</summary>
    public static PlayerCharacter Create(Vector3 position, Races.CharacterProfile profile, bool applyStartingGrants)
    {
        var player = new PlayerCharacter
        {
            Name = "Player",
            DisplayName = "Player",
            TemplateId = "player",
            // Stable id so every player component persists under "<prefix>:player"
            // and reconnects to its saved state across sessions (see EntityComponent.SaveKey).
            PersistentId = "player",
            Position = position,
        };

        player.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        // The player's visible body (Phase 30B: the low-poly authored mesh, origin at the feet,
        // with socket_* equip attach points inside), framed by the third-person camera and ash-tinted
        // per corruption tier by the CorruptionAppearanceController. Rigging/animation is 30C.
        // glTF forward is +Z while Godot's is -Z, so the instance turns 180°.
        if (GD.Load<PackedScene>(PlayerModelPath)?.Instantiate() is Node3D bodyVisual)
        {
            bodyVisual.Name = "BodyMesh";
            bodyVisual.RotateY(Mathf.Pi);
            player.AddChild(bodyVisual);
            AttachWeaponVisual(bodyVisual);
        }
        else
        {
            // Model missing/unimported — keep the old stand-in capsule so the game stays playable.
            player.AddChild(new MeshInstance3D
            {
                Name = "BodyMesh",
                Mesh = new CapsuleMesh { Radius = 0.36f, Height = 1.75f },
                Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f) },
            });
        }

        AttributeSet attributes = GD.Load<AttributeSet>(PlayerAttributesPath) ?? AttributeSet.CreateDefault();
        player.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes, HealthRegen = 3f });
        player.AddChild(new LocomotionComponent { Name = "Locomotion" });
        player.AddChild(new CombatComponent { Name = "Combat", Team = PlayerTeam });
        player.AddChild(new InventoryComponent { Name = "Inventory" });
        player.AddChild(BuildHurtbox());

        // Pitch pivot at eye height; the first-person camera rides the pivot directly
        // (yaw turns the body, pitch tilts the pivot — same mechanics as the old orbit).
        var cameraPivot = new Node3D
        {
            Name = "CameraPivot",
            Position = new Vector3(0f, EyeHeight, 0f),
        };
        player.AddChild(cameraPivot);
        var camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Position = Vector3.Zero,
            Near = 0.08f, // tight near plane so world geometry hugs the eye without clipping weirdness
        };
        cameraPivot.AddChild(camera);
        camera.AddChild(new Embervale.Combat.CameraShake { Name = "Shake" });

        // Melee swing volume in front of the body; opened by the weapon component.
        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, 1.0f, -1.1f),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.4f, 1.6f) },
        });
        player.AddChild(hitbox);

        WeaponResource? weapon = GD.Load<WeaponResource>(StartingWeaponPath);
        player.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = weapon,
            Hitbox = hitbox,
        });
        player.AddChild(new HitReactionComponent { Name = "HitReaction" });
        // 30C: plays the rig's idle/run/block/attack/hit/death clips off combat/locomotion state.
        player.AddChild(new Embervale.Animation.CharacterAnimationComponent { Name = "Animation" });
        player.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        player.AddChild(new DodgeComponent { Name = "Dodge" });
        player.AddChild(new LockOnComponent { Name = "LockOn" });

        // Equipment sits after inventory + weapon so it can resolve both; the
        // starting weapon above becomes the baseline restored on unequip.
        player.AddChild(new EquipmentComponent { Name = "Equipment" });

        // Progression before perks: perks spend the skill points progression awards.
        player.AddChild(new ProgressionComponent { Name = "Progression", CurvePath = ProgressionPath });
        player.AddChild(new PerksComponent { Name = "Perks" });

        // Quest log after progression + inventory so it resolves both for rewards.
        player.AddChild(new QuestLogComponent { Name = "QuestLog" });

        // Crafting: knows the starter recipes and consumes/produces through the inventory.
        player.AddChild(new CraftingComponent
        {
            Name = "Crafting",
            StartingRecipeIds = new Godot.Collections.Array<string>
            {
                GameIds.Recipes.IronIngot,
                GameIds.Recipes.LeatherStrips,
                GameIds.Recipes.HealthPotion,
                GameIds.Recipes.LeatherCap,
                GameIds.Recipes.SteelSword,
                GameIds.Recipes.IronRing,
            },
        });

        // Story flags: persistent conversation/world memory read & written by dialogue.
        player.AddChild(new StoryFlagsComponent { Name = "StoryFlags" });

        // Hotbar: quick-use bar (1-5) the player assigns from the inventory; resolves bag + equipment.
        player.AddChild(new HotbarComponent { Name = "Hotbar" });

        // Reputation: tracks standing with every faction and reacts to kills the player lands.
        player.AddChild(new ReputationComponent { Name = "Reputation" });

        // Corruption: the LORE's defining mechanic; a 0-100 meter feeding dialogue/factions/
        // abilities/appearance and the Dawnfire vs Lord of Embers endings (Phase 23).
        player.AddChild(new CorruptionComponent { Name = "Corruption" });

        // Corruption appearance: tints the placeholder body mesh per tier (Phase 23F stub; the
        // seam Phase 30's real models/VFX plug into).
        player.AddChild(new CorruptionAppearanceController { Name = "CorruptionAppearance" });

        // Magic: status effects can afflict/buff the player, and the spellbook aims
        // through the camera pivot so bolts fire where the player looks.
        player.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        player.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });
        player.AddChild(new SchoolMasteryComponent { Name = "SchoolMastery" });
        player.AddChild(new SpellcastingComponent
        {
            Name = "Spellcasting",
            AimNode = cameraPivot,
            KnownSpellIds = new Godot.Collections.Array<string>
            {
                GameIds.Spells.Firebolt,
                GameIds.Spells.Fireball,
                GameIds.Spells.FrostNova,
                GameIds.Spells.LesserHeal,
                GameIds.Spells.ArcaneShield,
                GameIds.Spells.FlameLance,
                GameIds.Spells.StormConduit,
            },
        });

        player.AddChild(new PlayerController
        {
            Name = "Controller",
            CameraPivot = cameraPivot,
            Camera = camera,
        });

        // Race applies LAST so Stats/Perks/Spellcasting/Reputation have initialized when its
        // OnInitialize runs: the chosen race's stat deltas become modifiers and (on New Game) its
        // innate perks/spells/reputation are granted (Phase 26C).
        player.AddChild(new Races.RaceComponent
        {
            Name = "Race",
            Profile = profile,
            ApplyStartingGrants = applyStartingGrants,
        });

        return player;
    }

    /// <summary>Hangs the visual sword (30C) off the rig's right-hand bone via a
    /// <see cref="BoneAttachment3D"/>, so it follows every animation clip. Purely cosmetic —
    /// hit timing/damage stay with <see cref="MeleeWeaponComponent"/> and its hitbox.</summary>
    private static void AttachWeaponVisual(Node bodyVisual)
    {
        if (FindSkeleton(bodyVisual) is not { } skeleton ||
            GD.Load<PackedScene>(WeaponModelPath)?.Instantiate() is not Node3D sword)
        {
            return;
        }

        string? handBone = null;
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string name = skeleton.GetBoneName(i);
            if (name.Contains("hand", System.StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith("R", System.StringComparison.OrdinalIgnoreCase))
            {
                handBone = name;
                break;
            }
        }

        if (handBone == null)
        {
            sword.QueueFree();
            return;
        }

        var attachment = new BoneAttachment3D { Name = "WeaponSocket", BoneName = handBone };
        skeleton.AddChild(attachment);
        attachment.AddChild(sword);
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

    private static Hurtbox BuildHurtbox()
    {
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });
        return hurtbox;
    }
}
