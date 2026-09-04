namespace Embervale.Core;

/// <summary>
/// Every production model path gameplay names, in one place.
///
/// This is the runtime half of the 3D asset contract (<c>docs/3D_ASSETS.md</c>). Gameplay code
/// refers to a model by a name from this class; nothing outside the asset pipeline should carry a
/// <c>res://assets/models/...</c> literal. The paths were previously spread across twelve files,
/// which meant renaming one model was a repo-wide grep and a silent failure when the grep missed
/// a caller — <see cref="Godot.GD.Load{T}"/> returns null for a bad path and the caller falls back
/// to a greybox, so the game keeps running and nobody finds out until someone looks at it.
///
/// The counterpart is <c>assets/models/manifest.json</c>, which is derived from the files on disk
/// by <c>python tools/assets.py status --write</c>. <c>ContentValidator.ValidateModelAssets</c>
/// checks every name here against both the manifest and the actual resource, so a path that stops
/// resolving fails <c>--validate</c> instead of quietly greyboxing.
///
/// ⚠️ Scene-placed models are NOT here and are not meant to be. The ~534 <c>ext_resource</c>
/// entries in <c>scenes/</c> are direct engine references resolved at load time; the engine already
/// fails loudly on a broken one, and routing them through a wrapper scene would buy nothing for the
/// cost of touching every cell in the world.
/// </summary>
public static class ModelAssets
{
    /// <summary>Where the pipeline puts adopted models.</summary>
    public const string Root = "res://assets/models/";

    /// <summary>For the placeable catalogue, which composes a path from an authored filename.</summary>
    public const string PropRoot = Root + "props/";

    // ⚠️ EVERY PATH BELOW IS A COMPLETE LITERAL, DELIBERATELY. Composing them from the root above
    // is tidier and was wrong: `assets.py` counts references by searching the repository for the
    // literal resource path, so a composed one is invisible to it and every model here reported as
    // unreferenced the moment it moved into this file. Keeping them whole also means a plain grep
    // for a filename still finds its callers, which is how anyone actually traces one.

    // HUMANOID — retargeted to GeneralSkeleton, driven by the shared animation library.
    public const string PlayerBody = "res://assets/models/characters/chr_player_base.glb";
    public const string Goblin = "res://assets/models/creatures/enm_goblin.glb";
    public const string AshenAcolyte = "res://assets/models/creatures/enm_ashen_acolyte.glb";

    // QUADRUPED — keeps its own rig and its own clips. Never retargeted; see docs/3D_ASSETS.md.
    public const string Horse = "res://assets/models/creatures/mnt_horse.glb";

    // FIRST-PERSON / VIEWMODEL — cosmetic, no collision, motion is procedural not baked.
    public const string FirstPersonArmRight = "res://assets/models/characters/fp_arm_right.glb";
    public const string FirstPersonArmLeft = "res://assets/models/characters/fp_arm_left.glb";

    // Equipment and weapons — rigid followers and hand-socket meshes.
    public const string IronSword = "res://assets/models/weapons/wpn_sword_iron.glb";
    public const string Pauldron = "res://assets/models/equipment/eqp_pauldron_embervale.glb";
    public const string Pouch = "res://assets/models/equipment/eqp_pouch_embervale.glb";

    /// <summary>The modular outfit kit every human NPC's profile draws its pieces from.</summary>
    public const string NpcKit = "res://assets/models/equipment/npc_kit_embervale.glb";

    /// <summary>Bolt-on identity pieces that give non-humanoid enemies their silhouette without
    /// forcing them through the humanoid rig.</summary>
    public const string EnemyIdentityKit = "res://assets/models/equipment/enemy_identity_kit.glb";

    // STATIC PROP — the ones gameplay code spawns directly rather than a scene placing.
    public const string TrainingDummy = "res://assets/models/props/prp_training_dummy.glb";
    public const string CacheChest = "res://assets/models/props/prp_cache_chest.glb";
    public const string CacheChestOpen = "res://assets/models/props/prp_cache_chest_open.glb";
    public const string TomeStand = "res://assets/models/props/prp_tome_stand.glb";

    /// <summary>The 46-clip shared library, extracted from its .glb so the source file's Mannequin
    /// mesh never ships. Regenerate with <c>python tools/assets.py build anim-library</c>.</summary>
    public const string AnimationLibrary = "res://assets/models/animations/anim_library.res";

    /// <summary>
    /// The full-body shared library (the 2026-09-04 combat/animation overhaul), built from Meshy
    /// clips by <c>tools/build_meshy_anim_library.gd</c>.
    ///
    /// ⚠️ <b>It is a different thing from <see cref="AnimationLibrary"/>, not a bigger one.</b> That
    /// one is upper-body and rotation-only by construction — its extractor strips every position
    /// track and all eight leg bones, because its Rigify source and the Quaternius bodies disagree
    /// about where the hips sit. This one comes off the same Meshy rig family as 31 of the 33
    /// humanoid bodies, so nothing has to be stripped and it can actually drive legs.
    /// </summary>
    public const string MeshyAnimationLibrary = "res://assets/models/animations/anim_meshy.res";

    /// <summary>The derived production manifest. Written by
    /// <c>python tools/assets.py status --write</c>; never edited by hand.</summary>
    public const string Manifest = "res://assets/models/manifest.json";

    /// <summary>Every model path above, for <c>ContentValidator</c> to check exists and is indexed.
    /// A path added to this class and forgotten here is the one failure this class cannot catch,
    /// so keep the two together.</summary>
    public static readonly string[] All =
    {
        PlayerBody, Goblin, AshenAcolyte, Horse,
        FirstPersonArmRight, FirstPersonArmLeft,
        IronSword, Pauldron, Pouch, NpcKit, EnemyIdentityKit,
        TrainingDummy, CacheChest, CacheChestOpen, TomeStand,
    };
}
