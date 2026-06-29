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
    private const string PlayerAttributesPath = "res://data/attributes/PlayerAttributes.tres";
    private const string StartingWeaponPath = "res://data/weapons/IronSword.tres";
    private const string ProgressionPath = "res://data/progression/PlayerProgression.tres";
    private const int PlayerTeam = 0;
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;

    // Third-person camera rig: the pivot sits at head height and the camera orbits it from
    // behind and slightly above, framing the player's body in the lower third of the view.
    private const float CameraPivotHeight = 1.55f;
    private const float CameraBackDistance = 3.8f;
    private const float CameraRise = 0.4f;

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

        // The player's visible body, framed by the third-person camera and tinted per corruption
        // tier by the CorruptionAppearanceController (Phase 23F). Phase 30 replaces this stand-in
        // capsule with the real rigged model.
        player.AddChild(new MeshInstance3D
        {
            Name = "BodyMesh",
            Mesh = new CapsuleMesh { Radius = 0.36f, Height = 1.75f },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.58f) },
        });

        AttributeSet attributes = GD.Load<AttributeSet>(PlayerAttributesPath) ?? AttributeSet.CreateDefault();
        player.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes });
        player.AddChild(new LocomotionComponent { Name = "Locomotion" });
        player.AddChild(new CombatComponent { Name = "Combat", Team = PlayerTeam });
        player.AddChild(new InventoryComponent { Name = "Inventory" });
        player.AddChild(BuildHurtbox());

        // Pitch pivot at head height; the camera hangs behind and above it so mouse-look orbits
        // the third-person camera around the player (yaw turns the body, pitch tilts the pivot).
        var cameraPivot = new Node3D
        {
            Name = "CameraPivot",
            Position = new Vector3(0f, CameraPivotHeight, 0f),
        };
        player.AddChild(cameraPivot);
        var camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Position = new Vector3(0f, CameraRise, CameraBackDistance),
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
        player.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        player.AddChild(new DodgeComponent { Name = "Dodge" });

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
            },
        });

        player.AddChild(new PlayerController
        {
            Name = "Controller",
            CameraPivot = cameraPivot,
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
