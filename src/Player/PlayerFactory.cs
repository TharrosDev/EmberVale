using Embervale.Combat.Actions;
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
using Embervale.Shrines;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Builds a fully-assembled player actor in code (hybrid first/third-person rig — the camera
/// mode is a live setting <see cref="PlayerCameraRig"/> owns). Constructing it
/// here (rather than a hand-authored <c>.tscn</c>) keeps the node graph, its
/// collision shape and its components in one reviewable place while the project
/// is young; it can be promoted to a packed scene later without changing callers.
/// </summary>
public static class PlayerFactory
{
    internal const string PlayerAttributesPath = "res://data/attributes/PlayerAttributes.tres";
    internal const string StartingWeaponPath = "res://data/weapons/IronSword.tres";
    internal const string ProgressionPath = "res://data/progression/PlayerProgression.tres";
    internal const string PlayerModelPath = ModelAssets.PlayerBody;
    internal const string WeaponModelPath = ModelAssets.IronSword;
    internal const string PauldronModelPath = ModelAssets.Pauldron;
    internal const string PouchModelPath = ModelAssets.Pouch;
    private const int PlayerTeam = 0;
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;

    // Hybrid camera: the pitch pivot sits at eye height, and the camera rides it directly in first
    // person or swings out behind-and-over-the-right-shoulder in third. PlayerCameraRig owns the
    // blend between the two and the wall spring; these are the third-person seat at full extension.
    // The shoulder offset is what keeps the body off the crosshair. The Phase 43 cutscene director
    // frames the same rig.
    private const float EyeHeight = 1.62f;
    internal const float ThirdPersonBackDistance = 3.8f;
    internal const float ThirdPersonRise = 0.4f;
    internal const float ThirdPersonShoulder = 0.6f;

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

        // The player's visible body, framed by the third-person camera and ash-tinted
        // per corruption tier by the CorruptionAppearanceController. Rigging/animation is 30C.
        // glTF forward is +Z while Godot's is -Z, so the instance turns 180°.
        if (GD.Load<PackedScene>(PlayerModelPath)?.Instantiate() is Node3D bodyVisual)
        {
            bodyVisual.Name = "BodyMesh";
            bodyVisual.RotateY(Mathf.Pi);
            player.AddChild(bodyVisual);
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
        player.AddChild(new FootstepComponent { Name = "Footsteps" });
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
        var shake = new Embervale.Combat.CameraShake { Name = "Shake", PlayerBody = player };
        camera.AddChild(shake);

        // Spells aim along this node rather than the pivot. It sits at the eye but AimController
        // re-aims it each frame at whatever the crosshair converges on, so a bolt goes where the
        // reticle is in third person too — from the pivot's raw forward it would miss by the
        // camera's pullback and shoulder offset. In first person the two are identical.
        var aimNode = new Node3D { Name = "AimPoint", Position = new Vector3(0f, EyeHeight, 0f) };
        player.AddChild(aimNode);

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
        player.AddChild(new CharacterActionComponent
        {
            Name = "Weapon",
            Weapon = weapon,
            Hitbox = hitbox,
        });
        player.AddChild(new HitReactionComponent { Name = "HitReaction" });
        // 30C: plays the rig's idle/run/block/attack/hit/death clips off combat/locomotion state.
        player.AddChild(new Embervale.Animation.CharacterAnimationComponent { Name = "Animation" });
        // The player's visible loadout: the drawn sword in the right hand, and Session 2's
        // protagonist layer — pauldrons on the upper arms and a utility pouch at the hips. Queued
        // rather than attached because the actor is built detached and has no skeleton yet; the
        // presentation component drains this the moment it finds the rig.
        var presentation = new Embervale.Animation.EquipmentPresentationComponent { Name = "EquipmentVisuals" };
        presentation.Pending.Add(new(
            Embervale.Animation.EquipmentSocket.HandR, WeaponModelPath, "MainHand",
            RotationDegrees: Embervale.Animation.WeaponGrip.HandRotationDegrees));
        presentation.Pending.Add(new(
            Embervale.Animation.EquipmentSocket.ShoulderL, PauldronModelPath, "PauldronLeft",
            Offset: new Vector3(0f, 0.015f, 0f)));
        presentation.Pending.Add(new(
            Embervale.Animation.EquipmentSocket.ShoulderR, PauldronModelPath, "PauldronRight",
            Offset: new Vector3(0f, 0.015f, 0f),
            RotationDegrees: new Vector3(0f, 180f, 0f)));
        presentation.Pending.Add(new(
            Embervale.Animation.EquipmentSocket.Hips, PouchModelPath, "UtilityPouch",
            Offset: new Vector3(-0.22f, 0.02f, 0.13f),
            RotationDegrees: new Vector3(5f, -8f, -8f)));
        player.AddChild(presentation);
        player.AddChild(new Embervale.Animation.FootIkComponent { Name = "FootIk" });
        player.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        player.AddChild(new DodgeComponent { Name = "Dodge" });
        player.AddChild(new LockOnComponent { Name = "LockOn", Camera = camera });
        // 39A: the mount rides ON this body rather than beside it, so it is a component of the
        // player and not an entity of its own. It reads the animation component and the camera
        // pivot, both of which exist by the time any OnInitialize runs.
        player.AddChild(new Embervale.Movement.MountComponent { Name = "Mount" });

        // Equipment sits after inventory + weapon so it can resolve both; the
        // starting weapon above becomes the baseline restored on unequip.
        player.AddChild(new EquipmentComponent
        {
            Name = "Equipment",
            // What goes back in the hand when a looted weapon is taken off.
            DefaultWeaponModelPath = WeaponModelPath,
        });

        // Progression before perks: perks spend the skill points progression awards.
        player.AddChild(new ProgressionComponent { Name = "Progression", CurvePath = ProgressionPath });
        player.AddChild(new PerksComponent { Name = "Perks" });

        // 41.5A: shrine visits persist as ids and re-derive their stat passives on load; shrines
        // themselves remain world callers, never a second save record.
        player.AddChild(new BlessingComponent { Name = "Blessings" });

        // Quest log after progression + inventory so it resolves both for rewards.
        player.AddChild(new QuestLogComponent { Name = "QuestLog" });

        // Crafting: knows the starter recipes and consumes/produces through the inventory. The list
        // lives in GameIds.Recipes.Starting so the content validator can check it against the recipe
        // database — nothing in the game teaches a recipe (CraftingComponent.Learn has no caller until
        // Phase 38's trainers), so anything missing from it is unreachable content. Gate a late recipe
        // on a scarce ingredient instead, the way drakescale mail gates on eight dragon scales.
        player.AddChild(new CraftingComponent
        {
            Name = "Crafting",
            StartingRecipeIds = new Godot.Collections.Array<string>(GameIds.Recipes.Starting),
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
            AimNode = aimNode,
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

        // ⚠️ THE SIX COMPONENTS BELOW ARE ONE SYSTEM AND THEIR ORDER MATTERS. The shared physics
        // queries go first because the rig, the sensor and the aim controller all resolve them; the
        // router goes last because it resolves all of the others. Each resolves its siblings in
        // OnInitialize, which runs as children become ready, so an earlier sibling is always there.
        player.AddChild(new PlayerPhysicsQueries { Name = "PhysicsQueries" });

        player.AddChild(new PlayerCameraRig
        {
            Name = "CameraRig",
            CameraPivot = cameraPivot,
            Camera = camera,
        });

        player.AddChild(new PlayerLookInput { Name = "LookInput" });
        player.AddChild(new InteractionSensor { Name = "Interaction" });
        player.AddChild(new AimController { Name = "Aim", AimNode = aimNode });
        player.AddChild(new PlayerInputRouter { Name = "InputRouter" });

        // The shake offsets around the rig's mode-aware rest pose — a fixed rest would snap the
        // camera back into the head after a crit while playing third-person. Looked up through the
        // player rather than captured, so the delegate cannot outlive the component it reads.
        shake.RestPosition = () => player.GetComponent<PlayerCameraRig>()?.CameraRestPosition ?? Vector3.Zero;

        // First-person viewmodel arms (30L): ride the camera, swing with attacks, guard on block.

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
